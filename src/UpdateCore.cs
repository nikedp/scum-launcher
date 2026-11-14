using System;
using System.IO;
using System.Net;
using System.Text;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
namespace SCUMLab {
public sealed class AssetSpec { public string name,sha256;public long bytes; }
public sealed class Channel {
    public int schemaVersion;public string version,tag,minLauncherVersion,clientSteamBuildId,serverSteamBuildId,serverAddress;
    public bool experimental;public AssetSpec[] files;public AssetSpec registryDelta;
}
public sealed class InstalledState { public string version,steamBuildId;public Dictionary<string,string> files=new Dictionary<string,string>(); }
public sealed class Change { public string name,oldHash,newHash,temporary;public bool oldExists,newExists; }
public sealed class Journal { public string status,version;public Change[] changes;public InstalledState oldState; }
public static class Data {
    public const string Repo="nikedp/scum-launcher",Version="0.1.4",RegistryName="pakchunk999-SCUMLabRegistry_P.pak";
    public static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=16*1024*1024,RecursionLimit=64};}
    public static T Read<T>(string file){return Json().Deserialize<T>(File.ReadAllText(file,Encoding.UTF8));}
    public static void Write(string file,object value){AtomicText(file,Json().Serialize(value));}
    public static void AtomicText(string file,string value){string temp=file+".tmp-"+Guid.NewGuid().ToString("N");File.WriteAllText(temp,value,new UTF8Encoding(false));if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);}
    public static string Hash(string file){using(FileStream f=File.OpenRead(file))using(SHA256 h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    public static void Require(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
    public static bool Owned(string name){return name!=null&&Regex.IsMatch(name,"^pakchunk[0-9]+-SCUMLab[A-Za-z0-9_-]*_P\\.pak$");}
    public static void Asset(AssetSpec a,bool pak){Require(a!=null&&a.name!=null&&Regex.IsMatch(a.sha256??"","^[a-fA-F0-9]{64}$")&&a.bytes>0&&a.bytes<=256*1024*1024,"Некорректный файл в манифесте.");Require(pak?Owned(a.name)&&a.name!=RegistryName:Regex.IsMatch(a.name,"^[A-Za-z0-9_-]+\\.json$"),"Недопустимое имя файла.");}
    public static void Validate(Channel c){
        Require(c!=null&&c.schemaVersion==1&&Regex.IsMatch(c.version??"","^[0-9]+\\.[0-9]+\\.[0-9]+(?:-[A-Za-z0-9.-]+)?$")&&c.tag=="v"+c.version,"Формат манифеста не поддерживается.");
        System.Version minimum;Require(System.Version.TryParse(c.minLauncherVersion,out minimum)&&new System.Version(Version)>=minimum,"Нужна новая версия лаунчера.");
        Require(Regex.IsMatch(c.clientSteamBuildId??"","^[0-9]+$")&&Regex.IsMatch(c.serverSteamBuildId??"","^[0-9]+$"),"В манифесте не указана версия SCUM.");
        Require(c.files!=null&&c.files.Length>0&&c.files.Length<=32,"Некорректный список файлов.");HashSet<string> names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(AssetSpec a in c.files){Asset(a,true);Require(names.Add(a.name),"Повтор имени файла.");}Asset(c.registryDelta,false);
    }
}
public interface IReleaseSource { Channel Channel();void Download(AssetSpec asset,string tag,string destination); }
public sealed class GitHubSource:IReleaseSource {
    const string Base="https://github.com/"+Data.Repo+"/releases/";
    sealed class TimedClient:WebClient { protected override WebRequest GetWebRequest(Uri address){WebRequest r=base.GetWebRequest(address);r.Timeout=60000;HttpWebRequest h=r as HttpWebRequest;if(h!=null)h.ReadWriteTimeout=60000;return r;} }
    static WebClient Client(){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;WebClient c=new TimedClient();c.Headers[HttpRequestHeader.UserAgent]="SCUMLabLauncher/"+Data.Version;return c;}
    public Channel Channel(){using(WebClient c=Client()){byte[] b=c.DownloadData(Base+"latest/download/channel.json");Data.Require(b.Length<1024*1024,"Манифест слишком большой.");Channel value=Data.Json().Deserialize<Channel>(Encoding.UTF8.GetString(b).TrimStart('\ufeff'));Data.Validate(value);return value;}}
    public void Download(AssetSpec a,string tag,string destination){Data.Require(Regex.IsMatch(tag,"^v[0-9A-Za-z._-]+$"),"Некорректный тег.");using(WebClient c=Client())c.DownloadFile(Base+"download/"+Uri.EscapeDataString(tag)+"/"+Uri.EscapeDataString(a.name),destination);Data.Require(new FileInfo(destination).Length==a.bytes&&String.Equals(Data.Hash(destination),a.sha256,StringComparison.OrdinalIgnoreCase),"Контрольная сумма загрузки не совпала: "+a.name);}
}
public sealed class GameInstall {
    public string Root,Paks,Exe,Manifest,BuildId;public bool Server;
    internal static string NormalizeRoot(string root){
        Data.Require(!String.IsNullOrWhiteSpace(root),"Выберите папку SCUM кнопкой «Выбрать…».");root=root.Trim();
        if(root.Length>=2&&root[0]=='\"'&&root[root.Length-1]=='\"')root=root.Substring(1,root.Length-2).Trim();
        Data.Require(!String.IsNullOrWhiteSpace(root),"Выберите папку SCUM кнопкой «Выбрать…».");
        Data.Require((Regex.IsMatch(root,@"^[A-Za-z]:[\\/]")||root.StartsWith(@"\\",StringComparison.Ordinal))&&root.IndexOfAny(new[]{'\0','\r','\n','*','?','\"','<','>','|'})<0,"Укажите полный путь к папке SCUM или выберите её кнопкой «Выбрать…».");
        try{return Path.GetFullPath(root);}catch(ArgumentException){throw new InvalidDataException("Не удалось прочитать путь. Выберите папку SCUM кнопкой «Выбрать…».");}catch(NotSupportedException){throw new InvalidDataException("Этот путь не поддерживается. Выберите папку SCUM кнопкой «Выбрать…».");}catch(PathTooLongException){throw new InvalidDataException("Путь слишком длинный. Выберите корневую папку SCUM.");}
    }
    public static GameInstall Open(string root){
        root=NormalizeRoot(root);string paks=Path.Combine(root,"SCUM","Content","Paks"),client=Path.Combine(root,"SCUM","Binaries","Win64","SCUM.exe"),server=Path.Combine(root,"SCUM","Binaries","Win64","SCUMServer.exe");
        Data.Require(Directory.Exists(paks)&&(File.Exists(client)||File.Exists(server)),"Выбери папку SCUM, внутри которой находится SCUM\\Content\\Paks.");
        GameInstall g=new GameInstall{Root=root,Paks=paks,Server=!File.Exists(client),Exe=File.Exists(client)?client:server};string app=g.Server?"3792580":"513710";
        List<string> candidates=new List<string>{Path.Combine(root,"steamapps","appmanifest_"+app+".acf")};DirectoryInfo parent=Directory.GetParent(root);if(parent!=null&&parent.Parent!=null)candidates.Add(Path.Combine(parent.Parent.FullName,"appmanifest_"+app+".acf"));
        foreach(string file in candidates)if(File.Exists(file)){g.Manifest=file;break;}Data.Require(g.Manifest!=null,"Не найден Steam appmanifest установленной игры.");
        string manifestText=File.ReadAllText(g.Manifest);g.BuildId=Regex.Match(manifestText,"\"buildid\"\\s+\"([0-9]+)\"").Groups[1].Value;Data.Require(g.BuildId.Length>0,"Steam Build ID не найден.");Data.Require(Regex.Match(manifestText,"\"StateFlags\"\\s+\"([0-9]+)\"").Groups[1].Value=="4","Steam ещё не завершил установку/обновление SCUM.");return g;
    }
    public static string Find(){string[] paths=GameDiscovery.Find(false).paths;return paths.Length==1?paths[0]:"";}
    public static void RequireStopped(){foreach(string name in new[]{"SCUM","SCUMServer","SCUM-Win64-Shipping","SCUMServer-Win64-Shipping"}){Process[] processes=Process.GetProcessesByName(name);try{Data.Require(processes.Length==0,"Закрой SCUM и сервер перед изменением файлов.");}finally{foreach(Process p in processes)p.Dispose();}}}
}
public sealed class UpdateService {
    public readonly GameInstall Game;public readonly string Work,StatePath,Transactions;public Action<string> Log;readonly IReleaseSource Source;
    internal Action StopCheck=GameInstall.RequireStopped;
    internal Action<int> AfterWrite;
    public UpdateService(GameInstall game,IReleaseSource source,Action<string> log){Game=game;Source=source;Log=log??delegate{};Work=Path.Combine(game.Root,"SCUMLabLauncherData");StatePath=Path.Combine(Work,"installed.json");Transactions=Path.Combine(Work,"transactions");}
    public InstalledState State(){return File.Exists(StatePath)?Data.Read<InstalledState>(StatePath):null;}
    void NoLinks(){foreach(string p in new[]{Game.Root,Path.Combine(Game.Root,"SCUM"),Path.Combine(Game.Root,"SCUM","Content"),Game.Paks,Work})if(Directory.Exists(p))Data.Require((File.GetAttributes(p)&FileAttributes.ReparsePoint)==0,"Папка синхронизации содержит ссылку/junction. Выбери фактическую папку.");}
    public void Compatible(Channel c){Data.Validate(c);Data.Require(Game.BuildId==(Game.Server?c.serverSteamBuildId:c.clientSteamBuildId),"Сборка SCUM "+Game.BuildId+" не поддерживается этим релизом. Обнови игру через Steam или дождись совместимого релиза.");}
    public bool Verify(InstalledState state){if(state==null||state.steamBuildId!=Game.BuildId||state.files==null||state.files.Count<1)return false;foreach(var f in state.files)if(!Data.Owned(f.Key)||!File.Exists(Path.Combine(Game.Paks,f.Key))||!String.Equals(Data.Hash(Path.Combine(Game.Paks,f.Key)),f.Value,StringComparison.OrdinalIgnoreCase))return false;return true;}
    public void Sync(Channel c,bool allowExperimental){
        Compatible(c);Data.Require(!c.experimental||allowExperimental,"Подтверди установку экспериментальной сборки.");StopCheck();NoLinks();Directory.CreateDirectory(Work);
        using(FileStream locked=new FileStream(Path.Combine(Work,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
            Recover();InstalledState old=State();Data.Require(old==null||Verify(old),"Установленные файлы изменены вне лаунчера. Обновление остановлено, чтобы сохранить изменения.");
            string stage=Path.Combine(Work,"staging",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);Dictionary<string,string> files=new Dictionary<string,string>();
            foreach(AssetSpec a in c.files){Log("Загрузка "+a.name);string target=Path.Combine(stage,a.name);Source.Download(a,c.tag,target);Data.Require(new FileInfo(target).Length==a.bytes&&Data.Hash(target).Equals(a.sha256,StringComparison.OrdinalIgnoreCase),"Проверка загрузки не пройдена.");files[a.name]=Data.Hash(target);}
            string deltaFile=Path.Combine(stage,c.registryDelta.name);Source.Download(c.registryDelta,c.tag,deltaFile);Data.Require(new FileInfo(deltaFile).Length==c.registryDelta.bytes&&Data.Hash(deltaFile).Equals(c.registryDelta.sha256,StringComparison.OrdinalIgnoreCase),"Проверка дельты не пройдена.");
            Log("Проверка локальных PAK и сборка реестра…");string extracted=Path.Combine(stage,"local-registry");ExtractionResult result=RegistryReader.Extract(Game.Exe,Game.Paks,extracted);
            Data.Require(result.Errors.Count==0&&result.VerifiedPakCount==result.PakCount&&result.Registries.Count==1,"Нельзя однозначно получить исходный реестр: проверь файлы игры и наличие других модов.");
            byte[] merged=RegistryMerge.Merge(File.ReadAllBytes(Path.Combine(extracted,result.Registries[0].File)),Data.Read<RegistryDelta>(deltaFile));string registry=Path.Combine(stage,Data.RegistryName);RegistryPak.Write(registry,merged);files[Data.RegistryName]=Data.Hash(registry);
            StopCheck();NoLinks();Data.Require(GameInstall.Open(Game.Root).BuildId==Game.BuildId,"Steam изменил версию игры во время подготовки. Повтори проверку после обновления Steam.");Commit(stage,new InstalledState{version=c.version,steamBuildId=Game.BuildId,files=files},old);Log("Синхронизация завершена: "+c.version);
        }
    }
    // Testable transaction boundary; only our named files are ever touched.
    public void Commit(string stage,InstalledState next,InstalledState old){
        NoLinks();Directory.CreateDirectory(Transactions);string tx=Path.Combine(Transactions,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(tx);
        HashSet<string> names=new HashSet<string>(next.files.Keys,StringComparer.OrdinalIgnoreCase);if(old!=null)foreach(string n in old.files.Keys)names.Add(n);List<Change> changes=new List<Change>();
        foreach(string n in names){Data.Require(Data.Owned(n),"Недопустимый путь установки.");string target=Path.Combine(Game.Paks,n);bool exists=File.Exists(target);string before=exists?Data.Hash(target):null;
            Data.Require(!exists||(File.GetAttributes(target)&FileAttributes.ReparsePoint)==0,"Ссылка вместо файла PAK.");Data.Require(!exists||(old!=null&&old.files.ContainsKey(n)&&old.files[n]==before),"Файл с таким именем уже существует и не принадлежит этой установке лаунчера: "+n);
            string after;bool newExists=next.files.TryGetValue(n,out after);if(newExists)Data.Require(File.Exists(Path.Combine(stage,n))&&Data.Hash(Path.Combine(stage,n))==after,"Staging checksum mismatch");if(exists)File.Copy(target,Path.Combine(tx,n));changes.Add(new Change{name=n,oldExists=exists,newExists=newExists,oldHash=before,newHash=after,temporary=n+".scumlab-"+Guid.NewGuid().ToString("N")});}
        Journal journal=new Journal{status="pending",version=next.version,changes=changes.ToArray(),oldState=old};Data.Write(Path.Combine(tx,"journal.json"),journal);
        try{int step=0;foreach(Change c in journal.changes){string target=Path.Combine(Game.Paks,c.name);if(c.newExists){string tmp=Path.Combine(Game.Paks,c.temporary);File.Copy(Path.Combine(stage,c.name),tmp,false);if(File.Exists(target))File.Replace(tmp,target,null);else File.Move(tmp,target);}else File.Delete(target);if(AfterWrite!=null)AfterWrite(++step);}Data.Write(StatePath,next);journal.status="applied";Data.Write(Path.Combine(tx,"journal.json"),journal);}
        catch{Restore(tx,journal);throw;}
    }
    void Restore(string tx,Journal j){
        foreach(Change c in j.changes){Data.Require(Data.Owned(c.name)&&c.temporary!=null&&Regex.IsMatch(c.temporary,"^"+Regex.Escape(c.name)+"\\.scumlab-[a-f0-9]{32}$"),"Invalid recovery path");string target=Path.Combine(Game.Paks,c.name);if(File.Exists(target)){Data.Require((File.GetAttributes(target)&FileAttributes.ReparsePoint)==0,"Ссылка вместо PAK.");string hash=Data.Hash(target);Data.Require(hash==c.newHash||hash==c.oldHash,"Файл изменён после обновления; автоматический откат остановлен: "+c.name);}if(c.oldExists){string backup=Path.Combine(tx,c.name);Data.Require(File.Exists(backup)&&Data.Hash(backup)==c.oldHash,"Резервная копия повреждена.");}}
        foreach(Change c in j.changes){string target=Path.Combine(Game.Paks,c.name);if(c.oldExists){string tmp=target+".scumlab-restore-"+Guid.NewGuid().ToString("N");File.Copy(Path.Combine(tx,c.name),tmp);if(File.Exists(target))File.Replace(tmp,target,null);else File.Move(tmp,target);}else if(File.Exists(target))File.Delete(target);string pending=Path.Combine(Game.Paks,c.temporary);if(File.Exists(pending))File.Delete(pending);}
        if(j.oldState==null){if(File.Exists(StatePath))File.Delete(StatePath);}else Data.Write(StatePath,j.oldState);j.status="rolled-back";Data.Write(Path.Combine(tx,"journal.json"),j);
    }
    public void Recover(){if(!Directory.Exists(Transactions))return;foreach(string dir in Directory.GetDirectories(Transactions)){string file=Path.Combine(dir,"journal.json");if(File.Exists(file)){Journal j=Data.Read<Journal>(file);if(j.status=="pending"){StopCheck();Restore(dir,j);Log("Восстановлена незавершённая установка.");}}}}
    public void Rollback(){StopCheck();NoLinks();Data.Require(Directory.Exists(Work),"Нет установки для отката.");using(FileStream locked=new FileStream(Path.Combine(Work,"update.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){Recover();string[] dirs=Directory.GetDirectories(Transactions);Array.Sort(dirs,StringComparer.Ordinal);Array.Reverse(dirs);foreach(string dir in dirs){string file=Path.Combine(dir,"journal.json");if(File.Exists(file)){Journal j=Data.Read<Journal>(file);if(j.status=="applied"){Restore(dir,j);Log("Предыдущие файлы восстановлены.");return;}}}throw new InvalidOperationException("Нет резервной копии для отката.");}}
}
}
