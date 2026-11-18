using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Reflection;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
namespace SCUMLab {
public sealed class LauncherReleaseAsset {public string name,browser_download_url,digest;public long size;}
public sealed class LauncherRelease {public string tag_name;public bool draft,prerelease;public LauncherReleaseAsset[] assets;}
public sealed class PreparedLauncherUpdate {public string helper,jobFile;}
public sealed class SelfUpdater {
    sealed class Client:WebClient {protected override WebRequest GetWebRequest(Uri address){WebRequest r=base.GetWebRequest(address);r.Timeout=60000;HttpWebRequest h=r as HttpWebRequest;if(h!=null)h.ReadWriteTimeout=60000;return r;}}
    static Client Web(){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;Client c=new Client();c.Headers[HttpRequestHeader.UserAgent]="SCUMLabLauncher/"+Data.Version;return c;}
    public static bool IsNewer(string tag,string current){Version a,b;return Regex.IsMatch(tag??"","^v[0-9]+\\.[0-9]+\\.[0-9]+$")&&Version.TryParse(tag.Substring(1),out a)&&Version.TryParse(current,out b)&&a>b;}
    /// Launcher releases live on our own feed (no GitHub): <Feed>launcher/latest.json in the GitHub release shape
    /// {tag_name, draft, prerelease, assets:[{name:"SCUMLabLauncher.zip", browser_download_url, digest:"sha256:...", size}]}, ZIP at <Feed>launcher/<tag>/SCUMLabLauncher.zip.
    public static string ZipUrl(string baseUrl,string tag){return baseUrl+"launcher/"+Uri.EscapeDataString(tag)+"/SCUMLabLauncher.zip";}
    public LauncherRelease Latest(){using(Client c=Web()){c.CachePolicy=new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);byte[] bytes=c.DownloadData(Data.Feed+"launcher/latest.json?t="+DateTime.UtcNow.Ticks);Data.Require(bytes.Length<64*1024,"Ответ сервера обновлений слишком большой.");LauncherRelease release=Data.Json().Deserialize<LauncherRelease>(Encoding.UTF8.GetString(bytes).TrimStart('\ufeff'));Data.Require(release!=null&&!release.draft&&!release.prerelease&&Regex.IsMatch(release.tag_name??"","^v[0-9]+\\.[0-9]+\\.[0-9]+$"),"Релиз лаунчера не поддерживается.");return release;}}
    public PreparedLauncherUpdate Prepare(LauncherRelease release){
        Data.Require(release!=null&&IsNewer(release.tag_name,Data.Version),"Установлена актуальная версия лаунчера.");
        LauncherReleaseAsset asset=null;foreach(LauncherReleaseAsset item in release.assets??new LauncherReleaseAsset[0])if(item.name=="SCUMLabLauncher.zip"){Data.Require(asset==null,"Повтор ZIP в релизе.");asset=item;}
        Data.Require(asset!=null&&Regex.IsMatch(asset.digest??"","^sha256:[a-fA-F0-9]{64}$")&&asset.size>0&&asset.size<=64*1024*1024,"Не найдена проверяемая сборка лаунчера.");
        string expected=ZipUrl(Data.Feed,release.tag_name);Data.Require(asset.browser_download_url==expected,"Неожиданный адрес загрузки лаунчера.");
        string target=Assembly.GetEntryAssembly().Location;Data.Require(!String.IsNullOrWhiteSpace(target)&&File.Exists(target),"Не найден текущий EXE.");
        Data.Require(IsNewer(release.tag_name,AssemblyName.GetAssemblyName(target).Version.ToString(3)),"На диске уже установлена эта или более новая версия. Перезапустите лаунчер.");
        string probe=Path.Combine(Path.GetDirectoryName(target),".scumlab-update-probe-"+Guid.NewGuid().ToString("N"));
        try{using(FileStream f=new FileStream(probe,FileMode.CreateNew,FileAccess.Write))f.WriteByte(1);File.Delete(probe);}catch(UnauthorizedAccessException){throw new InvalidDataException("Нет прав на замену EXE. Перенесите лаунчер в доступную для записи папку или запустите его от имени администратора.");}
        string token=Guid.NewGuid().ToString("N"),folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SCUMLabLauncher","updates",token);Directory.CreateDirectory(folder);string zip=Path.Combine(folder,"launcher.zip");
        string lanBase=FeedSource.LanMirror;bool fetched=false;
        if(lanBase!=null){try{using(Client c=Web())c.DownloadFile(ZipUrl(lanBase,release.tag_name),zip);fetched=new FileInfo(zip).Length==asset.size&&("sha256:"+Data.Hash(zip)).Equals(asset.digest,StringComparison.OrdinalIgnoreCase);}catch{}if(!fetched){FeedSource.LanFailed();if(File.Exists(zip))File.Delete(zip);}}
        if(!fetched)using(Client c=Web())c.DownloadFile(expected,zip);Data.Require(new FileInfo(zip).Length==asset.size&&("sha256:"+Data.Hash(zip)).Equals(asset.digest,StringComparison.OrdinalIgnoreCase),"Контрольная сумма ZIP лаунчера не совпала.");
        string staged=Path.Combine(folder,"SCUMLabLauncher.exe");using(ZipArchive archive=ZipFile.OpenRead(zip)){ZipArchiveEntry selected=null;foreach(ZipArchiveEntry entry in archive.Entries)if(entry.FullName=="SCUMLabLauncher.exe"){Data.Require(selected==null,"В ZIP несколько EXE лаунчера.");selected=entry;}Data.Require(selected!=null&&selected.Length>0&&selected.Length<=64*1024*1024,"EXE в ZIP не найден или слишком большой.");using(Stream input=selected.Open())using(FileStream output=new FileStream(staged,FileMode.CreateNew,FileAccess.Write)){byte[] buffer=new byte[65536];int n;while((n=input.Read(buffer,0,buffer.Length))>0){output.Write(buffer,0,n);Data.Require(output.Length<=selected.Length,"Размер EXE превышает ожидаемый.");}Data.Require(output.Length==selected.Length,"EXE извлечён не полностью.");}}
        LauncherUpdateJob job;using(Process current=Process.GetCurrentProcess())job=new LauncherUpdateJob{target=target,staged=staged,oldSHA256=Data.Hash(target),newSHA256=Data.Hash(staged),expectedVersion=release.tag_name.Substring(1),token=token,parentPid=current.Id,parentStartedUtcTicks=current.StartTime.ToUniversalTime().Ticks};LauncherUpdateTransaction.Validate(job);
        string helper=Path.Combine(folder,"SCUMLabUpdateHelper.exe");using(Stream embedded=Assembly.GetExecutingAssembly().GetManifestResourceStream("SCUMLab.Updater.exe")){Data.Require(embedded!=null,"Помощник обновления не включён в сборку.");using(FileStream file=new FileStream(helper,FileMode.CreateNew,FileAccess.Write))embedded.CopyTo(file);}
        string jobFile=Path.Combine(folder,"job.json");Data.Write(jobFile,job);return new PreparedLauncherUpdate{helper=helper,jobFile=jobFile};
    }
    public void Start(PreparedLauncherUpdate update){Data.Require(update!=null&&File.Exists(update.helper)&&File.Exists(update.jobFile),"Обновление не подготовлено.");Process.Start(new ProcessStartInfo(update.helper,"--apply \""+update.jobFile+"\""){UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(update.helper)});}
}
}
