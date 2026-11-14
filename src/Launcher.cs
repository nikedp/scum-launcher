using System;
using System.IO;
using System.Drawing;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;
namespace SCUMLab {
sealed class PaletteButton:Button { protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);if(!Enabled)TextRenderer.DrawText(e.Graphics,Text,Font,ClientRectangle,Color.FromArgb(118,140,158),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);} }
public sealed class LauncherForm:Form {
    readonly TextBox pathBox=new TextBox(),output=new TextBox();readonly Label status=new Label(),details=new Label();readonly CheckBox experimental=new CheckBox();
    readonly Button check=new PaletteButton(),sync=new PaletteButton(),rollback=new PaletteButton(),play=new PaletteButton(),browse=new PaletteButton();bool busy;Channel channel;
    readonly string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SCUMLabLauncher","settings.json");
    sealed class Settings { public string root; }
    public LauncherForm(){
        Text="SCUM Lab Launcher";ClientSize=new Size(920,640);MinimumSize=new Size(880,640);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(13,20,28);ForeColor=Color.FromArgb(229,235,240);Font=new Font("Segoe UI",10);AutoScaleMode=AutoScaleMode.Dpi;
        Panel rail=new Panel{Dock=DockStyle.Left,Width=222,BackColor=Color.FromArgb(18,29,40),Padding=new Padding(24)};Controls.Add(rail);
        Label brand=new Label{Text="SCUM / LAB",Font=new Font("Segoe UI",20,FontStyle.Bold),Location=new Point(22,32),Size=new Size(194,42),ForeColor=Color.FromArgb(222,184,109)};rail.Controls.Add(brand);
        rail.Controls.Add(new Label{Text="Модификации сервера\nЛаунчер "+Data.Version,Location=new Point(25,82),Size=new Size(180,55),ForeColor=Color.FromArgb(154,173,190)});
        MakeButton(check,"Проверить обновления",rail,23,170,176);MakeButton(sync,"Синхронизировать",rail,23,226,176);sync.BackColor=Color.FromArgb(185,146,75);sync.ForeColor=Color.FromArgb(14,21,29);
        MakeButton(rollback,"Откатить файлы",rail,23,282,176);MakeButton(play,"Запустить SCUM",rail,23,412,176);play.Enabled=false;
        Label foot=new Label{Text="GitHub Releases\nnikedp/scum-launcher",Location=new Point(24,550),Size=new Size(184,48),ForeColor=Color.FromArgb(154,173,190),Cursor=Cursors.Hand};foot.Click+=(s,e)=>Process.Start("https://github.com/"+Data.Repo);rail.Controls.Add(foot);
        Label title=new Label{Text="Твоя сборка SCUM",Font=new Font("Segoe UI",22,FontStyle.Bold),Location=new Point(252,28),Size=new Size(620,46),Anchor=AnchorStyles.Left|AnchorStyles.Top|AnchorStyles.Right};Controls.Add(title);
        Controls.Add(new Label{Text="Проверка → синхронизация → игра",Location=new Point(255,82),Size=new Size(610,28),ForeColor=Color.FromArgb(154,173,190)});
        Controls.Add(new Label{Text="Папка установленной игры или сервера",Location=new Point(255,133),Size=new Size(600,26)});
        pathBox.SetBounds(255,166,515,30);pathBox.BackColor=Color.FromArgb(27,41,54);pathBox.ForeColor=ForeColor;pathBox.BorderStyle=BorderStyle.FixedSingle;pathBox.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;Controls.Add(pathBox);
        MakeButton(browse,"Обзор",this,782,164,105);browse.Anchor=AnchorStyles.Top|AnchorStyles.Right;
        status.SetBounds(255,227,625,38);status.Font=new Font("Segoe UI",15,FontStyle.Bold);status.ForeColor=Color.FromArgb(222,184,109);status.Text="Начни с проверки";Controls.Add(status);
        details.SetBounds(255,272,625,72);details.ForeColor=Color.FromArgb(174,190,204);details.Text="Лаунчер сверит версию SCUM и файлы сборки.\nРеестр создаётся из локальных игровых файлов.";details.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;Controls.Add(details);
        experimental.SetBounds(255,352,630,32);experimental.Text="Разрешить экспериментальную сборку";experimental.ForeColor=Color.FromArgb(222,184,109);Controls.Add(experimental);
        output.SetBounds(255,401,630,187);output.Multiline=true;output.ReadOnly=true;output.ScrollBars=ScrollBars.Vertical;output.BackColor=Color.FromArgb(17,27,37);output.ForeColor=Color.FromArgb(174,190,204);output.BorderStyle=BorderStyle.FixedSingle;output.Anchor=AnchorStyles.Left|AnchorStyles.Right|AnchorStyles.Top|AnchorStyles.Bottom;Controls.Add(output);
        Controls.Add(new Label{Text="Обновление и откат доступны после закрытия игры.",Location=new Point(255,603),Size=new Size(630,24),ForeColor=Color.FromArgb(154,173,190),Anchor=AnchorStyles.Left|AnchorStyles.Bottom});
        check.Click+=async(s,e)=>await Run(async()=>{GameInstall game=Open();channel=await Task.Run(()=>new GitHubSource().Channel());UpdateService service=Service(game);service.Compatible(channel);bool ready=await Task.Run(()=>service.Verify(service.State())&&service.State().version==channel.version);status.Text=ready?"Сборка синхронизирована":"Доступна сборка "+channel.version;details.Text=(game.Server?"Сервер":"Клиент")+" · Steam Build "+game.BuildId+"\n"+(channel.experimental?"Экспериментальный релиз: требуется игровой тест.":"Стабильный релиз.");play.Enabled=ready&&!game.Server;Log("Проверка завершена. Релиз "+channel.tag);});
        sync.Click+=async(s,e)=>await Run(async()=>{GameInstall game=Open();channel=await Task.Run(()=>new GitHubSource().Channel());Service(game).Compatible(channel);bool allow=experimental.Checked;await Task.Run(()=>Service(game).Sync(channel,allow));status.Text="Сборка синхронизирована";details.Text="Установлена версия "+channel.version+". Резервная копия сохранена.\nФайлы мира и настройки игры не изменяются лаунчером.";play.Enabled=!game.Server;});
        rollback.Click+=async(s,e)=>await Run(async()=>{GameInstall game=Open();await Task.Run(()=>Service(game).Rollback());status.Text="Откат файлов завершён";details.Text="Восстановлены предыдущие файлы модов.\nОткат файлов не отменяет изменения игрового мира.";play.Enabled=false;});
        browse.Click+=(s,e)=>{using(FolderBrowserDialog dialog=new FolderBrowserDialog{Description="Выбери корневую папку SCUM",SelectedPath=pathBox.Text,ShowNewFolderButton=false})if(dialog.ShowDialog(this)==DialogResult.OK){pathBox.Text=dialog.SelectedPath;play.Enabled=false;}};
        pathBox.TextChanged+=(s,e)=>{play.Enabled=false;};
        play.Click+=(s,e)=>{try{GameInstall game=Open();Data.Require(!game.Server&&Service(game).Verify(Service(game).State()),"Файлы сборки нужно проверить.");Process.Start("steam://rungameid/513710");}catch(Exception ex){Fail(ex);}};
        FormClosing+=(s,e)=>{if(busy){e.Cancel=true;Log("Дождись завершения операции. Прерванная установка будет восстановлена при следующей синхронизации.");}};
        try{pathBox.Text=File.Exists(settings)?Data.Read<Settings>(settings).root:GameInstall.Find();}catch{pathBox.Text="";}
    }
    void MakeButton(Button b,string text,Control parent,int x,int y,int width){b.Text=text;b.SetBounds(x,y,width,42);b.FlatStyle=FlatStyle.Flat;b.FlatAppearance.BorderColor=Color.FromArgb(55,73,90);b.BackColor=Color.FromArgb(26,42,56);b.ForeColor=ForeColor;b.Cursor=Cursors.Hand;parent.Controls.Add(b);}
    GameInstall Open(){GameInstall g=GameInstall.Open(pathBox.Text.Trim());Directory.CreateDirectory(Path.GetDirectoryName(settings));Data.Write(settings,new Settings{root=g.Root});return g;}
    UpdateService Service(GameInstall game){return new UpdateService(game,new GitHubSource(),Log);}
    void Log(string text){if(InvokeRequired){BeginInvoke(new Action<string>(Log),text);return;}output.AppendText(DateTime.Now.ToString("HH:mm:ss")+"  "+text+Environment.NewLine);}
    void Fail(Exception ex){status.Text="Операция остановлена";details.Text=ex is System.Net.WebException?"Не удалось получить релиз GitHub. Проверь подключение\nи наличие опубликованного релиза.":ex.Message;Log(ex.Message);play.Enabled=false;}
    async Task Run(Func<Task> work){if(busy)return;busy=true;check.Enabled=sync.Enabled=rollback.Enabled=browse.Enabled=pathBox.Enabled=experimental.Enabled=false;play.Enabled=false;try{await work();}catch(Exception ex){Fail(ex);}finally{busy=false;check.Enabled=sync.Enabled=rollback.Enabled=browse.Enabled=pathBox.Enabled=experimental.Enabled=true;}}
    public void Snapshot(string file){ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Location=new Point(-32000,-32000);CreateControl();Show();Application.DoEvents();using(Bitmap image=new Bitmap(Width,Height)){DrawToBitmap(image,new Rectangle(0,0,Width,Height));image.Save(file,System.Drawing.Imaging.ImageFormat.Png);}Close();}
}
internal static class Program {
    [STAThread] static int Main(string[] args){try{
        if(args.Length>0&&args[0]=="--self-test")return SelfTests.Run(args.Length>1?args[1]:Path.Combine(Path.GetTempPath(),"SCUMLab-self-test-"+Guid.NewGuid().ToString("N")));
        if(args.Length==4&&args[0]=="--registry-pak"){RegistryPak.Write(args[3],RegistryMerge.Merge(File.ReadAllBytes(args[1]),Data.Read<RegistryDelta>(args[2])));return 0;}
        Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length==2&&args[0]=="--screenshot"){new LauncherForm().Snapshot(args[1]);return 0;}Application.Run(new LauncherForm());return 0;
    }catch(Exception ex){if(args.Length>0){Console.Error.WriteLine(ex.ToString());return 1;}MessageBox.Show(ex.Message,"SCUM Lab Launcher",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}}
}
}
