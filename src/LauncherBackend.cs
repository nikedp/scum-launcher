using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace SCUMLab {

public sealed class LauncherSettings { public string root,server;public bool experimental=true; }

/// Mods/play state for the page. Computed off the UI thread (hashes files).
public sealed class LocalSnapshot {
    public string root,buildId,installedVersion,parkedVersion;
    public bool rootSet,valid,server;
    public string mods="nofolder";   // nofolder | error | server | none | off | on | missing | resync | modified
    public string modsHint="";
    public bool toggleAllowed;public string toggleLabel="Отключить моды";
    public string playLabel="Запустить SCUM";public bool playEnabled;public string playMode="none";   // lab | vanilla | none
    public string[] files=new string[0];
}

/// Everything the HTML page can ask for. Messages: {id,cmd,args} in; {type:"reply",id,ok,result|error} and
/// {type:"event",name,data} out. Only one long operation runs at a time; window commands and reads always answer.
public sealed class LauncherBackend {
    readonly WebLauncherForm form;readonly Action<string> send;readonly JavaScriptSerializer json=Data.Json();
    readonly string settingsPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SCUMLabLauncher","settings.json");
    LauncherSettings settings=new LauncherSettings();Channel channel;
    public bool Busy,ClosingForUpdate;

    public LauncherBackend(WebLauncherForm form,Action<string> send){
        this.form=form;this.send=send;
        try{if(File.Exists(settingsPath))settings=Data.Read<LauncherSettings>(settingsPath)??new LauncherSettings();}catch{settings=new LauncherSettings();}
        if(String.IsNullOrWhiteSpace(settings.server))settings.server=ServerStatus.DefaultAddress;
        if(!String.IsNullOrWhiteSpace(settings.root)&&!Directory.Exists(settings.root))settings.root=null;
    }

    // ---------- protocol ----------
    void Event(string name,object data){send(json.Serialize(new Dictionary<string,object>{{"type","event"},{"name",name},{"data",data}}));}
    void Reply(object id,bool ok,object result,string error){send(json.Serialize(new Dictionary<string,object>{{"type","reply"},{"id",id},{"ok",ok},{"result",result},{"error",error}}));}
    public void Log(string text){Event("log",new Dictionary<string,object>{{"time",DateTime.Now.ToString("HH:mm:ss")},{"text",text}});}
    void Status(string title,string text,int tone){Event("status",new Dictionary<string,object>{{"title",title},{"text",text},{"tone",tone}});}

    public async void Handle(string message){
        Dictionary<string,object> m;try{m=json.Deserialize<Dictionary<string,object>>(message);}catch{return;}
        if(m==null||!m.ContainsKey("cmd"))return;
        object id=m.ContainsKey("id")?m["id"]:null;string cmd=m["cmd"] as string;
        Dictionary<string,object> args=(m.ContainsKey("args")?m["args"]:null) as Dictionary<string,object>??new Dictionary<string,object>();
        try{object result=await Dispatch(cmd,args);Reply(id,true,result,null);}
        catch(Exception ex){string text=Friendly(ex);Reply(id,false,null,text);}
    }

    static string Friendly(Exception ex){
        if(ex is AggregateException&&ex.InnerException!=null)ex=ex.InnerException;
        return ex is System.Net.WebException?"Не удалось связаться с сервером обновлений. Проверьте подключение и повторите попытку.":ex.Message;
    }
    static string Arg(Dictionary<string,object> a,string key){object v;return a.TryGetValue(key,out v)&&v!=null?Convert.ToString(v):null;}
    static bool Flag(Dictionary<string,object> a,string key){object v;return a.TryGetValue(key,out v)&&v is bool&&(bool)v;}

    async Task<object> Dispatch(string cmd,Dictionary<string,object> a){
        switch(cmd){
            case "init":return Init();
            case "window":form.WindowCommand(Arg(a,"action")??"");return null;
            case "local":return await Task.Run(()=>Snapshot());
            case "server.status":return await ServerQuery();
            case "server.set":return SetServer(Arg(a,"address"));
            case "server.copy":Clipboard.SetText(settings.server);return settings.server;
            case "settings.experimental":settings.experimental=Flag(a,"value");Save();return settings.experimental;
            case "game.detect":return await Operation("Ищем установленную SCUM…",()=>Detect(Flag(a,"extended")));
            case "game.choose":return ChooseFolder();
            case "game.set":return await SetRoot(Arg(a,"root"));
            case "game.openFolder":RequireRoot();Process.Start(new ProcessStartInfo("explorer.exe","\""+settings.root+"\""){UseShellExecute=true});return null;
            case "update.check":return await Operation("Проверяем обновления…",Check);
            case "update.sync":return await Operation("Синхронизируем сборку…",Sync);
            case "update.rollback":return await Operation("Восстанавливаем предыдущую версию…",Rollback);
            case "mods.toggle":return await Operation("Переключаем моды…",Toggle);
            case "game.play":return await Task.Run(()=>Play());
            case "client.clear":return await Operation("Очищаем клиентские данные…",ClearClient);
            case "client.backups":return OpenBackups();
            case "launcher.update":return await Operation("Проверяем версию лаунчера…",UpdateLauncher);
            case "launcher.check":return await LauncherCheck();
            case "open":OpenExternal(Arg(a,"url"));return null;
            default:throw new InvalidOperationException("Неизвестная команда: "+cmd);
        }
    }

    object Init(){
        return new Dictionary<string,object>{{"version",Data.Version},{"root",settings.root},{"server",settings.server},{"experimental",settings.experimental},{"repo",Data.Repo}};
    }

    async Task<object> Operation(string title,Func<Task<object>> work){
        Data.Require(!Busy,"Уже выполняется другая операция. Дождитесь её завершения.");
        Busy=true;Event("busy",new Dictionary<string,object>{{"on",true},{"title",title}});
        object result=null;string failure=null;
        try{result=await work();}
        catch(Exception ex){failure=Friendly(ex);}
        Busy=false;Event("busy",new Dictionary<string,object>{{"on",false},{"title",""}});
        if(failure!=null){Status("Не удалось завершить операцию",failure,3);Log(failure);}
        await PushLocal();
        if(ClosingForUpdate){form.BeginInvoke(new Action(form.Close));}
        if(failure!=null)throw new OperationFailed(failure);
        return result;
    }
    sealed class OperationFailed:Exception{public OperationFailed(string m):base(m){}}

    async Task PushLocal(){try{LocalSnapshot s=await Task.Run(()=>Snapshot());Event("local",s);}catch{}}

    // ---------- settings / folder ----------
    void Save(){try{Directory.CreateDirectory(Path.GetDirectoryName(settingsPath));Data.Write(settingsPath,settings);}catch(Exception ex){Log("Не удалось сохранить настройки: "+ex.Message);}}
    void RequireRoot(){Data.Require(!String.IsNullOrWhiteSpace(settings.root),"Сначала укажите папку игры в «Настройках».");}
    GameInstall Open(){RequireRoot();return GameInstall.Open(settings.root);}
    UpdateService Service(GameInstall game){
        FeedSource source=new FeedSource();int index=0;string last=null;DateTime started=DateTime.UtcNow;long startBytes=0;
        int count=channel!=null&&channel.files!=null?channel.files.Length+1:0;
        source.Progress=(name,done,total)=>{
            if(name!=last){last=name;index++;started=DateTime.UtcNow;startBytes=done;}
            double seconds=(DateTime.UtcNow-started).TotalSeconds;double speed=seconds>0.5?(done-startBytes)/seconds:0;
            Event("progress",new Dictionary<string,object>{{"file",name},{"index",index},{"count",count},{"done",done},{"total",total},{"speed",(long)speed}});
        };
        source.Note=Log;
        return new UpdateService(game,source,Log);
    }

    object SetServer(string address){
        string host;int port;Data.Require(ServerStatus.TryParseAddress(address,out host,out port),"Адрес сервера в формате хост:порт, например 100.125.34.42:7779.");
        settings.server=address.Trim();Save();return settings.server;
    }

    async Task<object> SetRoot(string root){
        GameInstall game=GameInstall.Open(root);
        settings.root=game.Root;Save();Log("Папка игры: "+game.Root);
        LocalSnapshot s=await Task.Run(()=>Snapshot());Event("local",s);return game.Root;
    }

    object ChooseFolder(){
        using(FolderBrowserDialog d=new FolderBrowserDialog{Description="Выберите корневую папку SCUM — папку установки из Steam",SelectedPath=Directory.Exists(settings.root??"")?settings.root:"",ShowNewFolderButton=false}){
            return d.ShowDialog(form)==DialogResult.OK?d.SelectedPath:null;
        }
    }

    async Task<object> Detect(bool extended){
        Status("Ищем установленную SCUM…",extended?"Проверяем библиотеки Steam и локальные диски, обычно до 15 секунд.":"Проверяем библиотеки Steam на всех дисках.",0);
        GameSearchResult r=await Task.Run(()=>GameDiscovery.Find(extended));
        if(r.paths.Length==0){Status("Клиент SCUM не найден",extended?"Проверены доступные папки. Укажите путь вручную.":"Попробуйте «Искать на дисках» или укажите путь вручную.",2);Log(r.limited?"Поиск ограничен по времени или глубине.":"SCUM не найдена в проверенных папках.");}
        else if(r.paths.Length==1){await SetRoot(r.paths[0]);Status("Клиент SCUM найден","Путь сохранён. Можно проверить обновления.",1);}
        else Status("Найдено несколько установок","Выберите нужную в списке.",0);
        return new Dictionary<string,object>{{"paths",r.paths},{"limited",r.limited}};
    }

    // ---------- local state (port of LauncherForm.RefreshLocal) ----------
    LocalSnapshot Snapshot(){
        LocalSnapshot s=new LocalSnapshot{root=settings.root,rootSet=!String.IsNullOrWhiteSpace(settings.root)};
        if(!s.rootSet){s.modsHint="Укажите папку игры в «Настройках».";return s;}
        GameInstall game;try{game=GameInstall.Open(settings.root);}catch(Exception ex){s.mods="error";s.modsHint=ex.Message;return s;}
        s.valid=true;s.server=game.Server;s.buildId=game.BuildId;
        if(game.Server){s.mods="server";s.modsHint="Серверная установка: переключение модов нужно только клиенту.";return s;}
        UpdateService service=new UpdateService(game,new FeedSource(),null);
        if(service.Parked){ParkedState p=null;try{p=service.ParkedInfo();}catch{}s.parkedVersion=p!=null?p.version:null;
            s.mods="off";s.modsHint="Игра без модов: можно на любые серверы. Файлы сохранены, вернуть — одной кнопкой.";s.toggleAllowed=true;s.toggleLabel="Вернуть моды";s.playLabel="Запустить SCUM";s.playEnabled=true;s.playMode="vanilla";return s;}
        InstalledState state=null;try{state=service.State();}catch{}
        if(state==null){s.mods="none";s.modsHint="Сборка SCUM Lab не установлена. Нажмите «Синхронизировать».";s.playEnabled=true;s.playMode="vanilla";return s;}
        s.installedVersion=state.version;s.files=state.files!=null?state.files.Keys.OrderBy(k=>k).ToArray():new string[0];
        bool ok=false;try{ok=service.Verify(state);}catch{}
        if(ok){s.mods="on";s.modsHint="Для других серверов — «Отключить моды»: файлы уберутся из игры и вернутся одной кнопкой.";s.toggleAllowed=true;s.playLabel="Играть";s.playEnabled=true;s.playMode="lab";return s;}
        bool missing=false,untouched=false;try{missing=service.AllMissing(state);untouched=!missing&&service.Untouched(state);}catch{}
        if(missing){s.mods="missing";s.modsHint="Файлов сборки нет в Paks\\SCUMLab: игра без модов. Для SCUM Lab — «Синхронизировать».";s.playEnabled=true;s.playMode="vanilla";}
        else if(untouched){s.mods="resync";s.modsHint="Версия SCUM изменилась или часть файлов удалена. Нажмите «Синхронизировать».";s.toggleAllowed=true;}
        else{s.mods="modified";s.modsHint="Файлы в Paks\\SCUMLab изменены вне лаунчера. Нажмите «Синхронизировать», чтобы восстановить.";}
        return s;
    }

    // ---------- operations ----------
    async Task<object> Check(){
        GameInstall game=Open();channel=await Task.Run(()=>new FeedSource().Channel());UpdateService service=Service(game);service.Compatible(channel);
        LocalSnapshot s=await Task.Run(()=>Snapshot());
        bool ready=s.mods=="on"&&s.installedVersion==channel.version;
        if(s.mods=="off")Status("Моды отключены","Последний релиз "+channel.version+". Для SCUM Lab нажмите «Вернуть моды» или «Синхронизировать».",2);
        else Status(ready?"Всё готово к игре":"Доступна сборка "+channel.version,(game.Server?"Сервер":"Клиент")+" · Steam Build "+game.BuildId+(s.installedVersion!=null?" · установлена "+s.installedVersion:"")+(ready?"":". Нажмите «Синхронизировать»."),ready?1:0);
        Log("Проверка завершена. Релиз "+channel.tag+(channel.experimental?" (экспериментальный)":""));
        return new Dictionary<string,object>{{"version",channel.version},{"ready",ready},{"experimental",channel.experimental},{"files",channel.files.Select(f=>new Dictionary<string,object>{{"name",f.name},{"bytes",f.bytes}}).ToArray()}};
    }

    async Task<object> Sync(){
        GameInstall game=Open();channel=await Task.Run(()=>new FeedSource().Channel());UpdateService service=Service(game);service.Compatible(channel);
        bool allow=settings.experimental;Data.Require(!channel.experimental||allow,"Релиз экспериментальный: включите «Разрешить экспериментальную сборку» в настройках.");
        await Task.Run(()=>service.Sync(channel,allow));
        Status("Сборка синхронизирована","Установлена версия "+channel.version+". Предыдущая сохранена для отката.",1);Log("Синхронизация завершена: "+channel.version);
        return channel.version;
    }

    async Task<object> Rollback(){
        GameInstall game=Open();await Task.Run(()=>Service(game).Rollback());
        Status("Предыдущая версия восстановлена","Откат файлов завершён. Изменения игрового мира не отменяются.",1);return null;
    }

    async Task<object> Toggle(){
        GameInstall game=Open();UpdateService service=Service(game);bool parked=service.Parked;
        if(parked)await Task.Run(()=>service.Enable());else await Task.Run(()=>service.Disable());
        Status(parked?"Моды возвращены":"Моды отключены",parked?"Можно играть на SCUM Lab.":"Игра без модов: можно на другие серверы.",1);return !parked;
    }

    object Play(){
        GameInstall game=Open();Data.Require(!game.Server,"Запуск доступен только для клиента SCUM.");UpdateService service=Service(game);
        InstalledState st=service.Parked?null:service.State();
        if(service.Parked||st==null||service.AllMissing(st)){GameLaunch.StartVanilla();Log("SCUM запущен через Steam (без модов SCUM Lab).");return "vanilla";}
        Data.Require(service.Verify(st),"Файлы сборки не совпадают. Нажмите «Синхронизировать».");
        GameLaunch.Start(game);Log("SCUM запущен с модами SCUM Lab. Подключение: Direct Connect "+settings.server);return "lab";
    }

    async Task<object> ClearClient(){
        ClientMaintenance.RequireClientStopped();
        ClientResetResult r=await Task.Run(()=>new ClientMaintenance().Clear());
        Status(r.changed?"Клиентские данные сброшены":"Клиентские данные уже очищены",r.changed?"Прежняя папка сохранена в резервной копии.":"Папки %LOCALAPPDATA%\\SCUM нет, можно запускать игру.",1);
        if(r.changed)Log("Резервная копия клиентских данных: "+r.backup);return r.changed;
    }

    object OpenBackups(){
        string path=new ClientMaintenance().BackupRoot;Data.Require(Directory.Exists(path),"Резервных копий пока нет.");
        Process.Start(new ProcessStartInfo("explorer.exe","\""+path+"\""){UseShellExecute=true});return null;
    }

    /// Silent check for a newer launcher on our feed (no busy state): {current, latest, newer}; errors -> newer=false, error text.
    async Task<object> LauncherCheck(){
        try{LauncherRelease release=await Task.Run(()=>new SelfUpdater().Latest());
            return new Dictionary<string,object>{{"current",Data.Version},{"latest",release.tag_name},{"newer",SelfUpdater.IsNewer(release.tag_name,Data.Version)}};}
        catch(Exception ex){return new Dictionary<string,object>{{"current",Data.Version},{"latest",null},{"newer",false},{"error",ex.Message}};}
    }

    async Task<object> UpdateLauncher(){
        SelfUpdater updater=new SelfUpdater();LauncherRelease release=await Task.Run(()=>updater.Latest());
        if(!SelfUpdater.IsNewer(release.tag_name,Data.Version)){Status("Лаунчер актуален","Установлена версия "+Data.Version+".",1);return false;}
        Status("Обновляем лаунчер до "+release.tag_name,"После проверки загрузки окно закроется и откроется снова.",0);
        PreparedLauncherUpdate prepared=await Task.Run(()=>updater.Prepare(release));updater.Start(prepared);ClosingForUpdate=true;return true;
    }

    async Task<object> ServerQuery(){
        string address=settings.server;ServerStatusResult r;
        try{r=await Task.Run(()=>ServerStatus.Query(address));}catch{r=new ServerStatusResult();}
        return new Dictionary<string,object>{{"address",address},{"online",r.online},{"players",r.players},{"maxPlayers",r.maxPlayers},{"name",r.name}};
    }

    /// Only GitHub pages of this project and the official WebView2 page open outside.
    public static void OpenExternal(string url){
        if(url==null)return;
        bool allowed=url.StartsWith("https://github.com/"+Data.Repo,StringComparison.Ordinal)||url.StartsWith("https://developer.microsoft.com/microsoft-edge/webview2",StringComparison.Ordinal);
        if(allowed)try{Process.Start(new ProcessStartInfo(url){UseShellExecute=true});}catch{}
    }
}
}
