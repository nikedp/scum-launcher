using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace SCUMLab {

/// WebView2 plumbing. The SDK assemblies and WebView2Loader.dll travel inside the EXE as resources:
/// managed ones load from memory, the native loader is extracted once per hash under %LOCALAPPDATA%.
/// Nothing here may be touched before Install() runs (types from the SDK resolve through it).
public static class WebRuntime {
    static bool installed;
    static readonly string Root=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SCUMLabLauncher");
    public static string DataFolder{get{return Path.Combine(Root,"webview2-data");}}

    public static void Install(){
        if(installed)return;installed=true;
        AppDomain.CurrentDomain.AssemblyResolve+=(s,e)=>{
            string name=new AssemblyName(e.Name).Name;
            if(name!="Microsoft.Web.WebView2.Core"&&name!="Microsoft.Web.WebView2.WinForms")return null;
            byte[] bytes=Resource("lib/"+name+".dll");return bytes==null?null:Assembly.Load(bytes);
        };
    }

    public static byte[] Resource(string name){
        using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream(name)){
            if(s==null)return null;using(MemoryStream m=new MemoryStream()){s.CopyTo(m);return m.ToArray();}
        }
    }

    /// Extracts WebView2Loader.dll and returns the installed Evergreen runtime version, or null if there is none.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static string Prepare(){
        Install();
        byte[] loader=Resource("lib/WebView2Loader.dll");if(loader==null)return null;
        string hash;using(SHA256 sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(loader)).Replace("-","").Substring(0,16).ToLowerInvariant();
        string folder=Path.Combine(Root,"webview2-loader",hash),file=Path.Combine(folder,"WebView2Loader.dll");
        if(!File.Exists(file)||new FileInfo(file).Length!=loader.Length){Directory.CreateDirectory(folder);string tmp=file+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllBytes(tmp,loader);try{File.Move(tmp,file);}catch{try{File.Delete(tmp);}catch{}}}
        return Probe(folder);
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    static string Probe(string folder){
        try{CoreWebView2Environment.SetLoaderDllFolderPath(folder);return CoreWebView2Environment.GetAvailableBrowserVersionString();}
        catch{return null;}
    }
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void Run(bool devTools){Application.Run(new WebLauncherForm(devTools));}
}

/// Borderless window: the whole UI is HTML from the EXE's resources (ui/*), served on https://app.scumlab.invalid/.
public sealed class WebLauncherForm:Form {
    const string Host="https://app.scumlab.invalid/";
    readonly WebView2 view=new WebView2();readonly LauncherBackend backend;readonly bool devTools;
    [DllImport("user32.dll")]static extern bool ReleaseCapture();
    [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);

    public WebLauncherForm(bool devTools){
        this.devTools=devTools;
        Text="SCUM Lab Launcher";ClientSize=new Size(1120,720);MinimumSize=new Size(980,640);StartPosition=FormStartPosition.CenterScreen;FormBorderStyle=FormBorderStyle.None;
        BackColor=Color.FromArgb(0x2A,0x31,0x22);Padding=new Padding(4);AutoScaleMode=AutoScaleMode.Dpi;
        try{Icon=Icon.ExtractAssociatedIcon(Application.ExecutablePath);}catch{}
        view.Dock=DockStyle.Fill;view.DefaultBackgroundColor=Color.FromArgb(0x0D,0x10,0x0B);Controls.Add(view);
        backend=new LauncherBackend(this,Send);
        FormClosing+=(s,e)=>{if(backend.Busy&&!backend.ClosingForUpdate){e.Cancel=true;backend.Log("Дождитесь завершения операции. Прерванная установка восстанавливается при следующей синхронизации.");}};
        Shown+=async(s,e)=>await Start();
    }

    async Task Start(){
        Rectangle area=Screen.FromControl(this).WorkingArea;if(Width>area.Width||Height>area.Height){Size=new Size(Math.Min(Width,area.Width),Math.Min(Height,area.Height));Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);}
        CoreWebView2EnvironmentOptions options=new CoreWebView2EnvironmentOptions();options.Language="ru";
        CoreWebView2Environment env=await CoreWebView2Environment.CreateAsync(null,WebRuntime.DataFolder,options);
        await view.EnsureCoreWebView2Async(env);
        CoreWebView2 core=view.CoreWebView2;CoreWebView2Settings st=core.Settings;
        st.AreDevToolsEnabled=devTools;st.AreDefaultContextMenusEnabled=devTools;st.IsStatusBarEnabled=false;st.IsZoomControlEnabled=false;st.AreBrowserAcceleratorKeysEnabled=devTools;st.IsPasswordAutosaveEnabled=false;st.IsGeneralAutofillEnabled=false;st.AreHostObjectsAllowed=false;
        core.AddWebResourceRequestedFilter(Host+"*",CoreWebView2WebResourceContext.All);
        core.WebResourceRequested+=(s,e)=>{
            string path=new Uri(e.Request.Uri).AbsolutePath.TrimStart('/');if(path.Length==0)path="index.html";
            byte[] bytes=path.Contains("..")?null:WebRuntime.Resource("ui/"+path);
            if(bytes==null){e.Response=env.CreateWebResourceResponse(null,404,"Not Found","");return;}
            e.Response=env.CreateWebResourceResponse(new MemoryStream(bytes),200,"OK","Content-Type: "+Mime(path)+"\r\nCache-Control: no-store");
        };
        core.NavigationStarting+=(s,e)=>{if(!e.Uri.StartsWith(Host,StringComparison.Ordinal)){e.Cancel=true;LauncherBackend.OpenExternal(e.Uri);}};
        core.NewWindowRequested+=(s,e)=>{e.Handled=true;LauncherBackend.OpenExternal(e.Uri);};
        core.WebMessageReceived+=(s,e)=>{string json;try{json=e.WebMessageAsJson;}catch{return;}backend.Handle(json);};
        core.Navigate(Host+"index.html");
    }

    static string Mime(string path){
        string ext=Path.GetExtension(path).ToLowerInvariant();
        switch(ext){case ".html":return "text/html; charset=utf-8";case ".css":return "text/css; charset=utf-8";case ".js":return "text/javascript; charset=utf-8";case ".svg":return "image/svg+xml";case ".png":return "image/png";case ".woff2":return "font/woff2";case ".json":return "application/json";default:return "application/octet-stream";}
    }

    /// Posts a JSON message to the page; safe from any thread.
    void Send(string json){
        if(IsDisposed)return;
        Action post=()=>{if(view.CoreWebView2!=null)view.CoreWebView2.PostWebMessageAsJson(json);};
        if(InvokeRequired){try{BeginInvoke(post);}catch(ObjectDisposedException){}catch(InvalidOperationException){}}else post();
    }

    public void WindowCommand(string what){
        if(what=="minimize")WindowState=FormWindowState.Minimized;
        else if(what=="maximize"){MaximizedBounds=Screen.FromControl(this).WorkingArea;WindowState=WindowState==FormWindowState.Maximized?FormWindowState.Normal:FormWindowState.Maximized;}
        else if(what=="close")Close();
        else if(what=="drag"&&WindowState==FormWindowState.Normal){ReleaseCapture();SendMessage(Handle,0xA1,new IntPtr(2),IntPtr.Zero);}
    }

    /// The 4px padding around the WebView is the resize border.
    protected override void WndProc(ref Message m){
        const int hit=0x84;
        if(m.Msg==hit&&WindowState==FormWindowState.Normal){Point p=PointToClient(new Point(unchecked((short)(long)m.LParam),unchecked((short)((long)m.LParam>>16))));int edge=6;bool left=p.X<edge,right=p.X>=Width-edge,top=p.Y<edge,bottom=p.Y>=Height-edge;int value=top?(left?13:right?14:12):bottom?(left?16:right?17:15):left?10:right?11:0;if(value!=0){m.Result=new IntPtr(value);return;}}
        base.WndProc(ref m);
    }
    protected override CreateParams CreateParams{get{CreateParams cp=base.CreateParams;cp.ClassStyle|=0x20000;cp.Style|=0x00020000;return cp;}}
}
}
