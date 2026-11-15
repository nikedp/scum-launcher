using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace SCUMLab {
internal static class MacTheme {
    public static readonly Color Canvas=Color.FromArgb(17,20,26),Rail=Color.FromArgb(21,25,32),Surface=Color.FromArgb(26,31,39),Ink=Color.FromArgb(235,240,246),Muted=Color.FromArgb(148,160,178),Blue=Color.FromArgb(98,232,170),Line=Color.FromArgb(48,57,70);
    public static GraphicsPath Round(RectangleF box,float radius){float d=radius*2;GraphicsPath p=new GraphicsPath();p.AddArc(box.X,box.Y,d,d,180,90);p.AddArc(box.Right-d,box.Y,d,d,270,90);p.AddArc(box.Right-d,box.Bottom-d,d,d,0,90);p.AddArc(box.X,box.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
    public static Font Font(float size,bool bold=false){return new Font("Segoe UI",size,bold?FontStyle.Bold:FontStyle.Regular);}
}
internal sealed class MacCard:Panel {
    public MacCard(){DoubleBuffered=true;BackColor=MacTheme.Canvas;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(GraphicsPath p=MacTheme.Round(new RectangleF(1,1,Width-3,Height-3),10))using(SolidBrush fill=new SolidBrush(MacTheme.Surface))using(Pen border=new Pen(MacTheme.Line)){e.Graphics.FillPath(fill,p);e.Graphics.DrawPath(border,p);}}
}
// Control has no native BUTTON window procedure, so Windows cannot paint square
// hover/focus edges over the custom surface (visible on some client systems).
internal sealed class MacButton:Control {
    bool hover,pressed,keyPressed;public bool Primary,Navigation,Destructive;
    public MacButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.Selectable,true);TabStop=true;AccessibleRole=AccessibleRole.PushButton;Cursor=Cursors.Hand;Font=MacTheme.Font(10);}
    protected override void OnParentChanged(EventArgs e){base.OnParentChanged(e);BackColor=Parent is MacCard?MacTheme.Surface:Parent==null?MacTheme.Canvas:Parent.BackColor;}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}
    protected override void OnMouseLeave(EventArgs e){hover=pressed=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){Focus();pressed=true;}Invalidate();base.OnMouseDown(e);}
    protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
    protected override void OnKeyDown(KeyEventArgs e){if(Enabled&&(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter)){pressed=keyPressed=true;e.Handled=true;Invalidate();}base.OnKeyDown(e);}
    protected override void OnKeyUp(KeyEventArgs e){if(keyPressed&&(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter)){keyPressed=pressed=false;e.Handled=true;Invalidate();OnClick(EventArgs.Empty);}base.OnKeyUp(e);}
    protected override void OnGotFocus(EventArgs e){base.OnGotFocus(e);Invalidate();}
    protected override void OnLostFocus(EventArgs e){keyPressed=pressed=false;base.OnLostFocus(e);Invalidate();}
    protected override void OnEnabledChanged(EventArgs e){Invalidate();base.OnEnabledChanged(e);}
    protected override void OnPaint(PaintEventArgs e){
        e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
        Color fill=Destructive?Color.FromArgb(65,34,43):Primary?MacTheme.Blue:Navigation?Color.FromArgb(35,57,51):Color.FromArgb(36,43,54);
        if(!Enabled)fill=Color.FromArgb(30,36,45);else if(pressed)fill=Destructive?Color.FromArgb(99,42,54):Primary?Color.FromArgb(62,187,132):Color.FromArgb(48,61,72);else if(hover)fill=Destructive?Color.FromArgb(83,39,50):Primary?Color.FromArgb(132,245,193):Color.FromArgb(45,57,69);
        using(GraphicsPath p=MacTheme.Round(new RectangleF(1,1,Width-3,Height-3),6))using(SolidBrush brush=new SolidBrush(fill))using(Pen border=new Pen(Navigation?Color.FromArgb(57,106,83):Primary?fill:MacTheme.Line)){e.Graphics.FillPath(brush,p);e.Graphics.DrawPath(border,p);}
        Color text=!Enabled?Color.FromArgb(112,126,145):Destructive?Color.FromArgb(255,126,143):Primary?Color.FromArgb(12,39,28):Navigation?MacTheme.Blue:MacTheme.Ink;
        TextRenderer.DrawText(e.Graphics,Text,Font,Rectangle.Inflate(ClientRectangle,-10,-2),text,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
        if(Focused&&ShowFocusCues)using(GraphicsPath p=MacTheme.Round(new RectangleF(4,4,Width-9,Height-9),4))using(Pen pen=new Pen(Primary?Color.FromArgb(20,68,47):MacTheme.Blue)){pen.DashStyle=DashStyle.Dot;e.Graphics.DrawPath(pen,p);}
    }
}
internal sealed class WindowDot:Control {
    public int Kind;bool hover;
    public WindowDot(int kind){Kind=kind;Size=new Size(34,30);TabStop=false;Cursor=Cursors.Hand;BackColor=MacTheme.Rail;SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);AccessibleRole=AccessibleRole.PushButton;AccessibleName=kind==0?"Закрыть":kind==1?"Свернуть":"Развернуть или восстановить";}
    protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){hover=false;Invalidate();base.OnMouseLeave(e);}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(hover?(Kind==0?Color.FromArgb(168,58,68):Color.FromArgb(42,51,64)):BackColor);using(Pen p=new Pen(MacTheme.Ink,1.2f)){if(Kind==0){e.Graphics.DrawLine(p,12,10,21,19);e.Graphics.DrawLine(p,12,19,21,10);}else if(Kind==1)e.Graphics.DrawLine(p,12,15,21,15);else e.Graphics.DrawRectangle(p,12,10,9,9);}}
}
internal sealed class DarkCheckBox:CheckBox {
    public DarkCheckBox(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=MacTheme.Surface;}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(BackColor);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;int y=(Height-14)/2;using(GraphicsPath p=MacTheme.Round(new RectangleF(1,y,14,14),3))using(SolidBrush b=new SolidBrush(Checked?MacTheme.Blue:MacTheme.Surface))using(Pen border=new Pen(Checked?MacTheme.Blue:MacTheme.Muted)){e.Graphics.FillPath(b,p);e.Graphics.DrawPath(border,p);}if(Checked)using(Pen pen=new Pen(Color.FromArgb(12,39,28),1.8f))e.Graphics.DrawLines(pen,new[]{new Point(4,y+7),new Point(7,y+10),new Point(12,y+4)});TextRenderer.DrawText(e.Graphics,Text,Font,new Rectangle(24,0,Width-25,Height),Enabled?ForeColor:Color.FromArgb(112,126,145),TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);if(Focused&&ShowFocusCues)ControlPaint.DrawFocusRectangle(e.Graphics,new Rectangle(22,2,Width-24,Height-4),MacTheme.Blue,BackColor);}
}
internal sealed class FolderField:Panel {
    public readonly TextBox Input=new TextBox();bool focused;
    [DllImport("user32.dll",CharSet=CharSet.Unicode,EntryPoint="SendMessageW")]static extern IntPtr Cue(IntPtr handle,int message,IntPtr wParam,string text);
    public FolderField(){DoubleBuffered=true;BackColor=MacTheme.Surface;Input.BorderStyle=BorderStyle.None;Input.Font=MacTheme.Font(10);Input.ForeColor=MacTheme.Ink;Input.BackColor=MacTheme.Surface;Controls.Add(Input);Input.HandleCreated+=(s,e)=>Cue(Input.Handle,0x1501,new IntPtr(1),"Выберите папку SCUM в Steam");Input.Enter+=(s,e)=>{focused=true;Invalidate();};Input.Leave+=(s,e)=>{focused=false;Invalidate();};Resize+=(s,e)=>Input.SetBounds(13,(Height-Input.PreferredHeight)/2,Math.Max(10,Width-26),Input.PreferredHeight);}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(GraphicsPath p=MacTheme.Round(new RectangleF(1,1,Width-3,Height-3),8))using(Pen pen=new Pen(focused?MacTheme.Blue:MacTheme.Line,focused?1.5f:1)){e.Graphics.DrawPath(pen,p);}}
}
internal sealed class AppMark:Control {
    public AppMark(){Size=new Size(46,46);SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Color.Transparent;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(GraphicsPath p=MacTheme.Round(new RectangleF(0,0,45,45),12))using(SolidBrush b=new SolidBrush(MacTheme.Blue))e.Graphics.FillPath(b,p);using(Font f=MacTheme.Font(17,true))TextRenderer.DrawText(e.Graphics,"SL",f,new Rectangle(0,0,46,46),Color.FromArgb(12,39,28),TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}
}
internal sealed class StatusGlyph:Control {
    public int State;public StatusGlyph(){Size=new Size(48,48);SetStyle(ControlStyles.SupportsTransparentBackColor|ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer,true);BackColor=Color.Transparent;}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;Color c=State==1?Color.FromArgb(44,153,105):State==2?Color.FromArgb(202,126,30):State==3?Color.FromArgb(208,75,72):MacTheme.Blue;using(SolidBrush b=new SolidBrush(Color.FromArgb(20,c)))e.Graphics.FillEllipse(b,1,1,45,45);using(Pen p=new Pen(c,2.1f)){p.StartCap=p.EndCap=LineCap.Round;if(State==1)e.Graphics.DrawLines(p,new[]{new Point(14,24),new Point(21,30),new Point(33,17)});else if(State==2||State==3){e.Graphics.DrawLine(p,24,13,24,26);using(SolidBrush b=new SolidBrush(c))e.Graphics.FillEllipse(b,22,31,4,4);}else{e.Graphics.DrawArc(p,13,13,22,22,35,285);e.Graphics.DrawLines(p,new[]{new Point(33,13),new Point(35,21),new Point(27,19)});}}}
}
internal sealed class ThinProgress:Control {
    readonly Timer timer=new Timer();int offset;public ThinProgress(){Height=3;DoubleBuffered=true;BackColor=MacTheme.Surface;timer.Interval=45;timer.Tick+=(s,e)=>{offset=(offset+8)%Math.Max(1,Width+150);Invalidate();};VisibleChanged+=(s,e)=>{if(Visible)timer.Start();else timer.Stop();};}
    protected override void OnPaint(PaintEventArgs e){e.Graphics.Clear(MacTheme.Line);using(SolidBrush b=new SolidBrush(MacTheme.Blue))e.Graphics.FillRectangle(b,offset-150,0,150,Height);}
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
}
}
