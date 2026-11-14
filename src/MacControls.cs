using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace SCUMLab {
internal static class MacTheme {
    public static readonly Color Canvas=Color.FromArgb(248,249,252),Rail=Color.FromArgb(239,241,245),Ink=Color.FromArgb(32,38,48),Muted=Color.FromArgb(120,126,138),Blue=Color.FromArgb(0,112,245),Line=Color.FromArgb(226,230,237);
    public static GraphicsPath Round(RectangleF box,float radius){float d=radius*2;GraphicsPath p=new GraphicsPath();p.AddArc(box.X,box.Y,d,d,180,90);p.AddArc(box.Right-d,box.Y,d,d,270,90);p.AddArc(box.Right-d,box.Bottom-d,d,d,0,90);p.AddArc(box.X,box.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    public static Font Font(float size,bool bold=false){return new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular);}
}
internal sealed class MacCard:Panel {
    public MacCard(){DoubleBuffered=true;SetStyle(ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;RectangleF b=new RectangleF(1,1,Width-3,Height-5);using(GraphicsPath shadow=MacTheme.Round(new RectangleF(1,3,Width-3,Height-5),16))using(SolidBrush ink=new SolidBrush(Color.FromArgb(12,30,45,70))){e.Graphics.FillPath(ink,shadow);}using(GraphicsPath p=MacTheme.Round(b,16))using(SolidBrush white=new SolidBrush(Color.White))using(Pen border=new Pen(Color.FromArgb(232,235,241))){e.Graphics.FillPath(white,p);e.Graphics.DrawPath(border,p);}}
}
internal sealed class MacButton:Button {
    bool hover,pressed;public bool Primary,Navigation;
    public MacButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor,true);BackColor=Color.Transparent;FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;Font=MacTheme.Font(10);}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left)pressed=true;Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;Color fill=Primary?MacTheme.Blue:Navigation?Color.FromArgb(220,231,249):Color.FromArgb(242,244,248);if(!Enabled)fill=Color.FromArgb(239,241,245);else if(pressed)fill=Primary?Color.FromArgb(0,86,204):Color.FromArgb(216,222,232);else if(hover)fill=Primary?Color.FromArgb(24,130,255):Color.FromArgb(229,234,242);RectangleF b=new RectangleF(1,1,Width-3,Height-3);using(GraphicsPath p=MacTheme.Round(b,9))using(SolidBrush brush=new SolidBrush(fill)){e.Graphics.FillPath(brush,p);if(!Primary&&!Navigation)using(Pen pen=new Pen(Color.FromArgb(226,230,237)))e.Graphics.DrawPath(pen,p);}Color text=!Enabled?Color.FromArgb(159,166,177):Primary?Color.White:Navigation?Color.FromArgb(25,86,167):MacTheme.Ink;TextRenderer.DrawText(e.Graphics,Text,Font,Rectangle.Inflate(ClientRectangle,-10,-2),text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);if(Focused&&ShowFocusCues)using(GraphicsPath p=MacTheme.Round(new RectangleF(3,3,Width-7,Height-7),7))using(Pen pen=new Pen(Primary?Color.White:MacTheme.Blue)){pen.DashStyle=DashStyle.Dot;e.Graphics.DrawPath(pen,p);}}
}
internal sealed class WindowDot:Button {
    public int Kind;bool hover;
    public WindowDot(int kind){Kind=kind;Size=new Size(24,28);FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;TabStop=false;Cursor=Cursors.Hand;BackColor=MacTheme.Rail;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);AccessibleName=kind==0?"Закрыть":kind==1?"Свернуть":"Развернуть или восстановить";}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;Color c=Kind==0?Color.FromArgb(255,95,87):Kind==1?Color.FromArgb(254,188,46):Color.FromArgb(40,200,64);using(SolidBrush b=new SolidBrush(c))using(Pen p=new Pen(Color.FromArgb(30,0,0,0))){e.Graphics.FillEllipse(b,6,8,12,12);e.Graphics.DrawEllipse(p,6,8,12,12);}if(hover)using(Pen p=new Pen(Color.FromArgb(110,50,45),1.2f)){if(Kind==0){e.Graphics.DrawLine(p,10,12,14,16);e.Graphics.DrawLine(p,10,16,14,12);}else if(Kind==1)e.Graphics.DrawLine(p,9,14,15,14);else e.Graphics.DrawRectangle(p,10,12,4,4);}}
}
internal sealed class FolderField:Panel {
    public readonly TextBox Input=new TextBox();bool focused;
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageW")]static extern IntPtr Cue(IntPtr handle,int message,IntPtr wParam,string text);
    public FolderField(){DoubleBuffered=true;BackColor=Color.White;Input.BorderStyle=BorderStyle.None;Input.Font=MacTheme.Font(10);Input.ForeColor=MacTheme.Ink;Input.BackColor=Color.White;Controls.Add(Input);Input.HandleCreated+=(s,e)=>Cue(Input.Handle,0x1501,new IntPtr(1),"Выберите папку SCUM в Steam");Input.Enter+=(s,e)=>{focused=true;Invalidate();};Input.Leave+=(s,e)=>{focused=false;Invalidate();};Resize+=(s,e)=>Input.SetBounds(13,(Height-Input.PreferredHeight)/2,Math.Max(10,Width-26),Input.PreferredHeight);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(GraphicsPath p=MacTheme.Round(new RectangleF(1,1,Width-3,Height-3),8))using(Pen pen=new Pen(focused?MacTheme.Blue:MacTheme.Line,focused?1.5f:1)){e.Graphics.DrawPath(pen,p);}}
}
internal sealed class AppMark:Control {
    public AppMark(){Size=new Size(46,46);SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Color.Transparent;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(GraphicsPath p=MacTheme.Round(new RectangleF(0,0,45,45),12))using(SolidBrush b=new SolidBrush(MacTheme.Blue))e.Graphics.FillPath(b,p);using(Font f=MacTheme.Font(23,true))TextRenderer.DrawText(e.Graphics,"S",f,new Rectangle(0,0,46,46),Color.White,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
}
internal sealed class StatusGlyph:Control {
    public int State;public StatusGlyph(){Size=new Size(48,48);SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Color.Transparent;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;Color c=State==1?Color.FromArgb(44,153,105):State==2?Color.FromArgb(202,126,30):State==3?Color.FromArgb(208,75,72):MacTheme.Blue;using(SolidBrush b=new SolidBrush(Color.FromArgb(20,c)))e.Graphics.FillEllipse(b,1,1,45,45);using(Pen p=new Pen(c,2.1f)){p.StartCap=p.EndCap=LineCap.Round;if(State==1)e.Graphics.DrawLines(p,new[]{new Point(14,24),new Point(21,30),new Point(33,17)});else if(State==2||State==3){e.Graphics.DrawLine(p,24,13,24,26);using(SolidBrush b=new SolidBrush(c))e.Graphics.FillEllipse(b,22,31,4,4);}else{e.Graphics.DrawArc(p,13,13,22,22,35,285);e.Graphics.DrawLines(p,new[]{new Point(33,13),new Point(35,21),new Point(27,19)});}}}
}
internal sealed class ThinProgress:Control {
    readonly Timer timer=new Timer();int offset;public ThinProgress(){Height=3;DoubleBuffered=true;BackColor=Color.White;timer.Interval=45;timer.Tick+=(s,e)=>{offset=(offset+8)%Math.Max(1,Width+150);Invalidate();};VisibleChanged+=(s,e)=>{if(Visible)timer.Start();else timer.Stop();};}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(Color.FromArgb(231,240,253));using(SolidBrush b=new SolidBrush(MacTheme.Blue))e.Graphics.FillRectangle(b,offset-150,0,150,Height);}
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
}
