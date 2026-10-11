using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Text;
using System.IO;
using System.Threading;
using System.Collections.Generic;

static class Native {
    public delegate IntPtr HookProc(int code, IntPtr message, IntPtr data);
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int x,y; }
    [StructLayout(LayoutKind.Sequential)] public struct Mouse { public Point pt; public uint mouseData, flags, time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct Keyboard { public ushort key, scan; public uint flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] public struct MouseInput { public int x,y; public uint data,flags,time; public UIntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] public struct Union { [FieldOffset(0)] public Keyboard keyboard; [FieldOffset(0)] public MouseInput mouse; }
    [StructLayout(LayoutKind.Sequential)] public struct Input { public uint type; public Union value; }
    [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr module, uint thread);
    [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr message,IntPtr data);
    [DllImport("kernel32.dll",CharSet=CharSet.Auto)] public static extern IntPtr GetModuleHandle(string name);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetClassName(IntPtr hwnd,StringBuilder name,int count);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll",SetLastError=true)] public static extern uint SendInput(uint count,Input[] input,int size);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hwnd,int command);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint process);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint first,uint second,bool attach);
    public static void FocusTestWindow(IntPtr hwnd) {
        uint pid; uint foregroundThread=GetWindowThreadProcessId(GetForegroundWindow(),out pid), current=GetCurrentThreadId();
        bool attached=foregroundThread!=0 && foregroundThread!=current && AttachThreadInput(current,foregroundThread,true);
        try { ShowWindow(hwnd,5); SetForegroundWindow(hwnd); } finally { if(attached) AttachThreadInput(current,foregroundThread,false); }
    }
    public static bool Modifiers() { foreach(int k in new int[]{0x10,0x11,0x12,0x5b,0x5c}) if((GetAsyncKeyState(k)&0x8000)!=0) return true; return false; }
    static Input Key(int key,bool up) { return new Input { type=1, value=new Union { keyboard=new Keyboard { key=(ushort)key,flags=up?2u:0u } } }; }
    public static bool Shortcut(int key,bool shift) {
        var input=new List<Input>(); input.Add(Key(0x11,false)); if(shift) input.Add(Key(0x10,false));
        input.Add(Key(key,false)); input.Add(Key(key,true)); if(shift) input.Add(Key(0x10,true)); input.Add(Key(0x11,true));
        if(SendInput((uint)input.Count,input.ToArray(),Marshal.SizeOf(typeof(Input)))==(uint)input.Count) return true;
        // Release our modifier keys if Windows accepted only part of the sequence.
        var release=new Input[]{Key(key,true),Key(0x10,true),Key(0x11,true)};
        SendInput((uint)release.Length,release,Marshal.SizeOf(typeof(Input))); return false;
    }
    public static void TestButton(int button) {
        Input[] a=new Input[2];
        a[0]=new Input { type=0,value=new Union { mouse=new MouseInput { data=(uint)button,flags=0x80 } } };
        a[1]=new Input { type=0,value=new Union { mouse=new MouseInput { data=(uint)button,flags=0x100 } } };
        if(SendInput(2,a,Marshal.SizeOf(typeof(Input)))!=2) throw new Exception("Test input failed");
    }
}

sealed class MacroForm : Form {
    readonly Label pathState=new Label(), copyState=new Label(), status=new Label(), lastButton=new Label();
    readonly Label activity=new Label(), pathButton=new Label(), copyButton=new Label(), pathHint=new Label(), copyHint=new Label(), pathStep=new Label(), copyStep=new Label();
    readonly Panel pathCard=new Panel(), copyCard=new Panel();
    readonly ToolTip tips=new ToolTip();
    static readonly Color Ink=Color.FromArgb(27,43,64), Muted=Color.FromArgb(89,106,125), Blue=Color.FromArgb(35,88,181), Line=Color.FromArgb(215,224,234);
    readonly Button pause=new Button(); readonly CheckBox swap=new CheckBox();
    readonly NotifyIcon tray=new NotifyIcon(); readonly System.Windows.Forms.Timer copyTimer=new System.Windows.Forms.Timer();
    Native.HookProc callback; IntPtr hook; volatile bool active=true; bool busy, pathReady, copyReady, quitting, capturePath;
    Thread hookThread; ApplicationContext hookContext;
    bool hooksStarted, backgroundMode;
    readonly bool testing; uint oldSequence; int attempts; IntPtr target; string savedPath;
    readonly bool[] swallowed=new bool[3]; readonly bool[] held=new bool[3];
    string config=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TumMakro","settings.txt");
    public string TestError; public bool PathReady { get { return pathReady; } } public bool CopyReady { get { return copyReady; } }
    public bool Busy { get { return busy; } } public bool Active { get { return active; } }
    public string Status { get { return status.Text; } }
    public MacroForm(bool test) {
        testing=test; Text="TumMakro · 1.1"; Font=new Font("Segoe UI",10); ClientSize=new Size(760,658);
        AutoScaleMode=AutoScaleMode.Dpi; StartPosition=FormStartPosition.CenterScreen;
        FormBorderStyle=FormBorderStyle.FixedSingle; MaximizeBox=false;
        BackColor=Color.FromArgb(244,247,251); ForeColor=Ink; Icon=SystemIcons.Application;
        AddLabel("TumMakro",28,22,500,43,25,FontStyle.Bold);
        AddLabel("Kopiera och klistra in med musens tumknappar",30,70,580,27,11,FontStyle.Regular);
        SetupLabel(activity,592,30,138,36); activity.TextAlign=ContentAlignment.MiddleCenter; activity.Font=new Font("Segoe UI",10,FontStyle.Bold);
        Panel guide=new Panel { BackColor=Color.FromArgb(229,238,251),Bounds=new Rectangle(28,115,704,68) }; Controls.Add(guide);
        CardText(guide,new Label(),"SÅ ANVÄNDER DU KNAPPARNA",16,9,672,19,9,FontStyle.Bold,Blue);
        CardText(guide,new Label(),"Markera  →  Tryck  →  Klicka i målet  →  Tryck samma knapp igen",16,33,672,26,11,FontStyle.Regular,Ink);
        BuildCard(pathCard,pathButton,pathStep,pathState,pathHint,28,"Sökväg till fil eller mapp","UTFORSKAREN");
        BuildCard(copyCard,copyButton,copyStep,copyState,copyHint,392,"Markerad text eller filer","CTRL+C  /  CTRL+V");
        swap.Text="Byt plats på knapparnas funktioner"; swap.SetBounds(30,398,690,30); swap.TabIndex=0; Controls.Add(swap);
        tips.SetToolTip(swap,"Byter vilken tumknapp som kopierar sökväg. Båda knapparna börjar om med kopiering.");
        try { if(!testing && File.Exists(config)) swap.Checked=File.ReadAllText(config).Trim()=="swap"; } catch { }
        swap.CheckedChanged+=delegate { Reset(); if(!testing) try { Directory.CreateDirectory(Path.GetDirectoryName(config)); File.WriteAllText(config,swap.Checked?"swap":"normal"); } catch { status.Text="Kunde inte spara knappordningen."; } };
        StyleButton(pause,"Pausa makron",28,449,166,44,true); pause.TabIndex=1; pause.Click+=delegate { TogglePause(); };
        Button reset=new Button(); StyleButton(reset,"Börja om med kopiering",206,449,254,44,false); reset.TabIndex=2; reset.Click+=delegate { Reset(); };
        tips.SetToolTip(reset,"Nästa tryck på båda knapparna kopierar. Innehållet i Windows urklipp finns kvar.");
        Button hide=new Button(); StyleButton(hide,"Dölj till klockan",472,449,180,44,false); hide.TabIndex=3; hide.Click+=delegate { Hide(); };
        Button help=new Button(); StyleButton(help,"Hjälp",664,449,68,44,false); help.TabIndex=4; help.Click+=delegate { ShowHelp(); };
        Panel feedback=new Panel { BackColor=Color.White,Bounds=new Rectangle(28,511,704,86) }; Controls.Add(feedback);
        CardText(feedback,new Label(),"SENASTE HÄNDELSE",16,10,660,17,9,FontStyle.Bold,Muted);
        CardText(feedback,status,"",16,34,672,40,10,FontStyle.Regular,Ink);
        SetupLabel(lastButton,30,609,702,20); lastButton.ForeColor=Muted; lastButton.Font=new Font("Segoe UI",9);
        lastButton.Text="Tryck en tumknapp för att se vilken knapp Windows rapporterar.";
        AddLabel("Kryss eller Dölj till klockan behåller makrona. Avsluta via ikonen vid klockan.",30,632,702,20,9,FontStyle.Regular);
        status.Text="Redo. Markera det du vill kopiera och tryck på en tumknapp.";
        ContextMenuStrip menu=new ContextMenuStrip(); menu.Items.Add("Öppna",null,delegate { Show(); WindowState=FormWindowState.Normal; Activate(); });
        menu.Items.Add("Pausa / aktivera makron",null,delegate { TogglePause(); }); menu.Items.Add("Börja om med kopiering",null,delegate { Reset(); });
        menu.Items.Add("Avsluta",null,delegate { quitting=true; Close(); }); tray.Icon=SystemIcons.Application; tray.ContextMenuStrip=menu;
        tray.DoubleClick+=delegate { Show(); Activate(); }; tray.Visible=!testing;
        copyTimer.Interval=40; copyTimer.Tick+=CheckCopy;
        FormClosing+=delegate(object sender,FormClosingEventArgs e) { if(!quitting && !testing && e.CloseReason==CloseReason.UserClosing) { e.Cancel=true; Hide(); } };
        Shown+=delegate { StartHooks(); };
        FormClosed+=delegate { if(backgroundMode) Application.ExitThread(); };
        UpdateStates();
    }
    void StartHooks() {
            if(hooksStarted) return;
            hooksStarted=true;
            callback=MouseHook; var ready=new ManualResetEvent(false);
            hookThread=new Thread(delegate() {
                hookContext=new ApplicationContext(); hook=Native.SetWindowsHookEx(14,callback,Native.GetModuleHandle(null),0);
                ready.Set(); if(hook!=IntPtr.Zero) Application.Run(hookContext);
            }); hookThread.IsBackground=true; hookThread.Start();
            ready.WaitOne(2000);
            if(hook==IntPtr.Zero) { active=false; status.Text="Kunde inte ansluta tumknapparna. Starta om programmet."; TestError="Hook installation failed"; } UpdateStates();
    }
    public void StartBackground() { backgroundMode=true; IntPtr window=Handle; StartHooks(); }
    public bool HookInstalled { get { return hook!=IntPtr.Zero; } }
    public bool TrayVisible { get { return tray.Visible; } }
    void AddLabel(string text,int x,int y,int w,int h,int size,FontStyle style) { Label l=new Label { Text=text,Font=new Font("Segoe UI",size,style),ForeColor=size<13?Muted:Ink }; SetupLabel(l,x,y,w,h); }
    void SetupLabel(Label l,int x,int y,int w,int h) { l.SetBounds(x,y,w,h); Controls.Add(l); }
    void CardText(Control parent,Label label,string text,int x,int y,int w,int h,int size,FontStyle style,Color color) { label.Text=text; label.Font=new Font("Segoe UI",size,style); label.ForeColor=color; label.SetBounds(x,y,w,h); parent.Controls.Add(label); }
    void BuildCard(Panel card,Label button,Label step,Label state,Label hint,int x,string title,string detail) {
        card.BackColor=Color.White; card.SetBounds(x,203,340,174); Controls.Add(card);
        card.Paint+=delegate(object sender,PaintEventArgs e) { using(Pen border=new Pen(Line)) e.Graphics.DrawRectangle(border,0,0,card.Width-1,card.Height-1); };
        CardText(card,button,"",18,13,304,22,10,FontStyle.Bold,Blue);
        CardText(card,new Label(),title,18,42,304,24,12,FontStyle.Bold,Ink);
        CardText(card,step,"",18,79,304,17,8,FontStyle.Bold,Muted);
        CardText(card,state,"",18,101,304,30,16,FontStyle.Bold,Blue);
        CardText(card,hint,"",18,139,304,24,9,FontStyle.Regular,Muted);
        card.AccessibleName=title; tips.SetToolTip(card,detail);
    }
    void StyleButton(Button button,string text,int x,int y,int w,int h,bool primary) {
        button.Text=text; button.SetBounds(x,y,w,h); button.FlatStyle=FlatStyle.Flat; button.FlatAppearance.BorderSize=1;
        button.FlatAppearance.BorderColor=primary?Blue:Line; button.BackColor=primary?Blue:Color.White;
        button.ForeColor=primary?Color.White:Ink; button.Font=new Font("Segoe UI",10,primary?FontStyle.Bold:FontStyle.Regular);
        button.UseVisualStyleBackColor=false; button.Cursor=Cursors.Hand; Controls.Add(button);
    }
    void ShowHelp() {
        MessageBox.Show(this,"SÖKVÄG\nMarkera en fil eller mapp i Utforskaren. Tryck sökvägsknappen, klicka i en textruta och tryck samma knapp igen.\n\nMARKERAD TEXT ELLER FILER\nMarkera innehållet. Första trycket skickar Ctrl+C, nästa skickar Ctrl+V.\n\nVILKEN TUMKNAPP?\nTryck en knapp och läs raden längst ned. Du kan byta plats på funktionerna.\n\nOM INGET HÄNDER\nStäll tumknapparna på Back/Bakåt och Forward/Framåt i Razer Synapse. Släpp Ctrl, Shift och Alt när du använder makrot.\n\nPAUS OCH AVSLUT\nPausa makron ger knapparna sina vanliga funktioner. Kryss döljer fönstret. Högerklicka på ikonen vid klockan och välj Avsluta för att stoppa programmet.","Hjälp med TumMakro",MessageBoxButtons.OK,MessageBoxIcon.Information);
    }
    public void TogglePause() { active=!active; Reset(); status.Text=active?"Aktiv. Nästa tryck på båda knapparna kopierar.":"Pausad. Tumknapparna fungerar som vanligt. Klicka Aktivera makron för att fortsätta."; UpdateStates(); }
    public void Reset() { copyTimer.Stop(); busy=false; pathReady=false; copyReady=false; savedPath=null; status.Text="Återställd. Nästa tryck kopierar."; UpdateStates(); }
    void UpdateStates() {
        activity.Text=active?"AKTIV":"PAUSAD";
        activity.BackColor=active?Color.FromArgb(220,242,230):Color.FromArgb(251,232,195);
        activity.ForeColor=active?Color.FromArgb(25,99,62):Color.FromArgb(125,82,20);
        pause.Text=active?"Pausa makron":"Aktivera makron";
        pathButton.Text=swap.Checked?"TUMKNAPP 2 · FRAMÅT":"TUMKNAPP 1 · BAKÅT";
        copyButton.Text=swap.Checked?"TUMKNAPP 1 · BAKÅT":"TUMKNAPP 2 · FRAMÅT";
        pathStep.Text=active?(pathReady?"NÄSTA TRYCK · STEG 2 AV 2":"NÄSTA TRYCK · STEG 1 AV 2"):"MAKROT ÄR PAUSAT";
        copyStep.Text=active?(copyReady?"NÄSTA TRYCK · STEG 2 AV 2":"NÄSTA TRYCK · STEG 1 AV 2"):"MAKROT ÄR PAUSAT";
        pathState.Text=!active?"Pausad":pathReady?"Klistra in sökväg":"Kopiera sökväg";
        copyState.Text=!active?"Pausad":copyReady?"Klistra in · Ctrl+V":"Kopiera · Ctrl+C";
        pathHint.Text=!active?"Knappen fungerar som vanligt.":pathReady?"Klicka där du vill ha sökvägen.":"Markera fil eller mapp i Utforskaren.";
        copyHint.Text=!active?"Knappen fungerar som vanligt.":copyReady?"Klicka där du vill klistra in.":"Markera text eller filer först.";
        pathState.ForeColor=copyState.ForeColor=active?Blue:Muted;
        pathCard.BackColor=active && pathReady?Color.FromArgb(237,244,255):Color.White;
        copyCard.BackColor=active && copyReady?Color.FromArgb(237,244,255):Color.White;
        tray.Text="TumMakro · "+(active?"aktiv":"pausad");
    }
    IntPtr MouseHook(int code,IntPtr msg,IntPtr data) {
        if(code>=0 && (msg.ToInt64()==0x20b || msg.ToInt64()==0x20c)) {
            Native.Mouse m=(Native.Mouse)Marshal.PtrToStructure(data,typeof(Native.Mouse));
            int b=(int)((m.mouseData>>16)&0xffff); bool down=msg.ToInt64()==0x20b;
            if(b>=1 && b<=2 && (testing || (m.flags&1)==0)) {
                if(!down) { held[b]=false; if(swallowed[b]) { swallowed[b]=false; return (IntPtr)1; } }
                else if(held[b]) { if(swallowed[b]) return (IntPtr)1; }
                else {
                    held[b]=true; swallowed[b]=active;
                    if(active) { IntPtr foreground=Native.GetForegroundWindow();
                        try { BeginInvoke((Action)delegate { if(!IsDisposed) Execute(b,foreground); }); } catch(InvalidOperationException) { }
                        return (IntPtr)1; }
                }
            }
        }
        return Native.CallNextHookEx(hook,code,msg,data);
    }
    void Execute(int button,IntPtr foreground) {
        lastButton.Text="Senaste tryck: tumknapp "+button;
        if(!active || busy) return;
        if(Native.Modifiers()) { status.Text="Släpp Ctrl, Shift, Alt och Windows-tangenten och försök igen."; return; }
        if(Native.GetForegroundWindow()!=foreground) { status.Text="Fönstret ändrades. Tryck igen i rätt fönster."; return; }
        bool path=swap.Checked?button==2:button==1;
        if(path && !pathReady) {
            StringBuilder cls=new StringBuilder(128); Native.GetClassName(foreground,cls,128);
            if(cls.ToString()!="CabinetWClass" && !(testing && foreground==Handle)) { status.Text="Öppna Utforskaren och markera filen eller mappen först."; return; }
        }
        if((path && pathReady) || (!path && copyReady)) {
            try {
                if(path) Clipboard.SetText(savedPath);
                if(Native.GetForegroundWindow()!=foreground) { status.Text="Fönstret ändrades. Tryck igen i rätt fönster."; return; }
                if(!Native.Shortcut(0x56,false)) { status.Text="Windows kunde inte skicka Ctrl+V till det här fönstret."; return; }
                if(path) { pathReady=false; savedPath=null; } else copyReady=false;
                status.Text="Skickade Ctrl+V. Nästa tryck kopierar igen."; UpdateStates();
            } catch { status.Text="Urklipp är upptaget. Försök klistra in igen."; }
            return;
        }
        oldSequence=Native.GetClipboardSequenceNumber(); target=foreground; capturePath=path; attempts=0; busy=true;
        if(!Native.Shortcut(0x43,path)) { busy=false; status.Text="Windows kunde inte skicka kopieringen till det här fönstret."; return; }
        if(!path) { busy=false; copyReady=true; status.Text="Skickade Ctrl+C. Nästa tryck skickar Ctrl+V."; UpdateStates(); return; }
        status.Text="Kopierar…"; copyTimer.Start();
    }
    void CheckCopy(object sender,EventArgs e) {
        attempts++;
        if(Native.GetClipboardSequenceNumber()!=oldSequence) {
            try {
                if(capturePath) {
                    string text=Clipboard.GetText(); string clean=CleanPaths(text);
                    if(clean==null) { FinishFailure("Ingen sökväg kopierades. Markera en fil i Utforskaren och försök igen."); return; }
                    savedPath=clean; pathReady=true;
                } else copyReady=true;
                copyTimer.Stop(); busy=false; status.Text="Kopierat. Nästa tryck på samma knapp klistrar in."; UpdateStates(); return;
            } catch { }
        }
        if(attempts>=30 || Native.GetForegroundWindow()!=target) FinishFailure("Ingen kopiering kunde bekräftas. Markera innehållet och försök igen.");
    }
    void FinishFailure(string message) { copyTimer.Stop(); busy=false; status.Text=message; }
    public static string CleanPaths(string text) {
        if(string.IsNullOrWhiteSpace(text)) return null;
        var lines=text.Split(new string[]{"\r\n","\n"},StringSplitOptions.RemoveEmptyEntries); var result=new List<string>();
        foreach(string line in lines) { string p=line.Trim().Trim('"'); if(!(p.Length>=3 && char.IsLetter(p[0]) && p[1]==':' && (p[2]=='\\' || p[2]=='/')) && !p.StartsWith(@"\\")) return null; result.Add(p); }
        return result.Count==0?null:string.Join(Environment.NewLine,result.ToArray());
    }
    protected override void Dispose(bool disposing) {
        active=false;
        if(hook!=IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook=IntPtr.Zero; }
        if(disposing) { if(hookContext!=null) hookContext.ExitThread(); copyTimer.Dispose(); tips.Dispose(); tray.Visible=false; tray.Dispose(); } base.Dispose(disposing);
    }
    public void Quit() { quitting=true; Close(); }
    public void SetSwap(bool value) { swap.Checked=value; }
    public void RenderPreview(string folder) {
        Directory.CreateDirectory(folder); hooksStarted=true; Show(); Application.DoEvents();
        using(Bitmap warmup=new Bitmap(Width,Height)) DrawToBitmap(warmup,new Rectangle(0,0,Width,Height));
        Application.DoEvents();
        using(Bitmap bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height)); bitmap.Save(Path.Combine(folder,"UI-kopiera.png")); }
        pathReady=true; copyReady=true; UpdateStates(); status.Text="Kopierat. Nästa tryck på samma knapp klistrar in.";
        using(Bitmap bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height)); bitmap.Save(Path.Combine(folder,"UI-klistra-in.png")); }
        TogglePause();
        using(Bitmap bitmap=new Bitmap(Width,Height)) { DrawToBitmap(bitmap,new Rectangle(0,0,Width,Height)); bitmap.Save(Path.Combine(folder,"UI-pausad.png")); }
    }
}

static class Program {
    [STAThread] static int Main(string[] args) {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        if(args.Length>1 && args[0]=="--render-ui") { using(MacroForm preview=new MacroForm(true)) preview.RenderPreview(args[1]); return 0; }
        if(args.Length>0 && args[0]=="--self-test") return SelfTest(args.Length>1?args[1]:"self-test.txt");
        if(args.Length>0 && args[0]=="--self-test-background") return BackgroundTest(args.Length>1?args[1]:"background-test.txt");
        bool background=Array.IndexOf(args,"--background")>=0;
        bool created; using(Mutex mutex=new Mutex(true,"Local\\TumMakro-v1",out created)) {
            if(!created) { if(!background) MessageBox.Show("TumMakro körs redan. Öppna det via ikonen vid klockan.","TumMakro"); return 0; }
            using(MacroForm form=new MacroForm(false)) {
                if(background) { form.StartBackground(); Application.Run(); } else Application.Run(form);
            } return 0;
        }
    }
    static int BackgroundTest(string file) {
        var report=new List<string>();
        try {
            using(MacroForm f=new MacroForm(false)) {
                f.StartBackground(); Pump(200);
                Assert(!f.Visible,"Background launch leaves window hidden",report);
                Assert(f.TrayVisible,"Background launch exposes tray icon",report);
                Assert(f.HookInstalled && f.Active,"Background launch installs active mouse hook",report);
                f.Show(); Pump(100); Assert(f.Visible && f.HookInstalled,"Tray window can open after background launch",report);
                f.Hide(); Pump(100); Assert(!f.Visible && f.HookInstalled,"Hiding window preserves active hook",report);
                f.Quit();
            }
            File.WriteAllLines(file,report.ToArray()); return 0;
        } catch(Exception e) { report.Add("FAIL: "+e.Message); File.WriteAllLines(file,report.ToArray()); return 1; }
    }
    static void Pump(int ms) { DateTime end=DateTime.UtcNow.AddMilliseconds(ms); do { Application.DoEvents(); Thread.Sleep(10); } while(DateTime.UtcNow<end); }
    static void WaitFor(Func<bool> condition) { DateTime end=DateTime.UtcNow.AddSeconds(4); while(!condition() && DateTime.UtcNow<end) Pump(40); Pump(100); }
    static void Assert(bool value,string name,List<string> report) { if(!value) throw new Exception(name); report.Add("PASS: "+name); }
    static int SelfTest(string file) {
        var report=new List<string>(); DataObject previous=null;
        try { IDataObject current=Clipboard.GetDataObject(); if(current!=null) { previous=new DataObject(); foreach(string format in current.GetFormats(false)) { object data=current.GetData(format,false); if(data!=null) previous.SetData(format,false,data); } } } catch { previous=null; }
        try {
            Assert(Marshal.SizeOf(typeof(Native.Input))==(IntPtr.Size==8?40:28),"Windows INPUT layout",report);
            Assert(MacroForm.CleanPaths("\"C:\\Test mapp\\fil.txt\"\r\n\"\\\\server\\share\\fil.txt\"")=="C:\\Test mapp\\fil.txt"+Environment.NewLine+"\\\\server\\share\\fil.txt","Local and network paths, multiple selections, quote removal",report);
            Assert(MacroForm.CleanPaths("ordinary text")==null && MacroForm.CleanPaths("")==null,"Reject non-path clipboard text",report);
            using(MacroForm f=new MacroForm(true)) {
                f.SetSwap(false); TextBox box=new TextBox { Multiline=true,Text="TumMakro copy test",Bounds=new Rectangle(24,65,530,42) }; f.Controls.Add(box); box.BringToFront();
                box.KeyDown+=delegate(object s,KeyEventArgs e) { if(e.Control && e.Shift && e.KeyCode==Keys.C) { Clipboard.SetText("\"C:\\TumMakro test\\sample.txt\""); e.SuppressKeyPress=true; } };
                f.TopMost=true; f.Show(); Pump(100); Native.FocusTestWindow(f.Handle); box.Focus(); Pump(300);
                Assert(f.TestError==null,"Global mouse hook installed",report);
                Assert(Native.GetForegroundWindow()==f.Handle,"Controlled test window focused",report);
                Native.FocusTestWindow(f.Handle); box.Focus(); Pump(100); box.SelectAll(); Native.TestButton(2);
                WaitFor(delegate { return f.CopyReady && Clipboard.ContainsText() && Clipboard.GetText()=="TumMakro copy test"; });
                Assert(f.CopyReady && Clipboard.GetText()=="TumMakro copy test","Mouse button 2 sends Ctrl+C and arms paste",report);
                box.Clear(); Native.TestButton(2); WaitFor(delegate { return box.Text=="TumMakro copy test"; });
                Assert(!f.CopyReady && box.Text=="TumMakro copy test","Second mouse button 2 press sends Ctrl+V and resets",report);
                Native.TestButton(1); WaitFor(delegate { return f.PathReady; }); Assert(f.PathReady,"Mouse button 1 arms path paste",report);
                Clipboard.SetText("intervening clipboard content"); box.Clear(); Native.FocusTestWindow(f.Handle); box.Focus(); Pump(100); Native.TestButton(1); WaitFor(delegate { return box.Text=="C:\\TumMakro test\\sample.txt"; });
                Assert(!f.PathReady && box.Text=="C:\\TumMakro test\\sample.txt","Path paste restores captured path and resets: "+f.Status+"; focused="+box.Focused+"; text length="+box.TextLength,report);
                f.TogglePause(); box.SelectAll(); Native.TestButton(2); Pump(150);
                Assert(!f.CopyReady && !f.Active,"Paused buttons do not run macros",report);
                f.TogglePause(); f.SetSwap(true); Native.FocusTestWindow(f.Handle); box.Focus(); box.Text="swapped copy"; box.SelectAll(); Pump(150); Native.TestButton(1); WaitFor(delegate { return f.CopyReady && Clipboard.ContainsText() && Clipboard.GetText()=="swapped copy"; });
                Assert(f.CopyReady && Clipboard.GetText()=="swapped copy","Swapped button 1 uses copy macro",report);
                f.Reset(); Assert(!f.CopyReady && !f.PathReady && !f.Busy,"Reset clears independent phases and pending copy",report);
                f.Quit();
            }
            report.Add("Controlled integration tests passed. Physical Razer buttons and File Explorer are not covered by this test.");
            File.WriteAllLines(file,report.ToArray()); return 0;
        } catch(Exception e) { report.Add("FAIL: "+e.Message); File.WriteAllLines(file,report.ToArray()); return 1; }
        finally { try { if(previous!=null) Clipboard.SetDataObject(previous,true); } catch { } }
    }
}
