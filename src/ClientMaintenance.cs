using System;
using System.IO;
using System.Diagnostics;
namespace SCUMLab {
public sealed class ClientResetResult {public bool changed;public string backup;}
public sealed class ClientMaintenance {
    readonly string local,source,backups;
    internal Action ClientStopped=RequireClientStopped;
    public ClientMaintenance():this(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)){}
    internal ClientMaintenance(string localAppData){local=Path.GetFullPath(localAppData);source=Path.Combine(local,"SCUM");backups=Path.Combine(local,"SCUMLabLauncher","client-backups");}
    public static void RequireClientStopped(){foreach(string name in new[]{"SCUM","SCUM-Win64-Shipping","SCUM_Launcher"}){Process[] processes=Process.GetProcessesByName(name);try{Data.Require(processes.Length==0,"Закройте клиент SCUM перед очисткой или восстановлением данных.");}finally{foreach(Process process in processes)process.Dispose();}}}
    static void NoLink(string path){if(Directory.Exists(path)||File.Exists(path))Data.Require((File.GetAttributes(path)&FileAttributes.ReparsePoint)==0,"Очистка папок со ссылками/junction не поддерживается.");}
    void Validate(){NoLink(local);NoLink(source);NoLink(Path.Combine(local,"SCUMLabLauncher"));NoLink(backups);Data.Require(!File.Exists(source),"Вместо папки SCUM найден файл.");}
    public string BackupRoot {get{return backups;}}
    public ClientResetResult Clear(){
        ClientStopped();Validate();if(!Directory.Exists(source))return new ClientResetResult{changed=false};
        Directory.CreateDirectory(backups);
        using(FileStream gate=new FileStream(Path.Combine(backups,"maintenance.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
            ClientStopped();Validate();if(!Directory.Exists(source))return new ClientResetResult{changed=false};
            string backup=Path.Combine(backups,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff")+"-"+Guid.NewGuid().ToString("N"));
            // Same-volume directory rename preserves every file (including saves),
            // without recursive deletion or traversal of any nested junction.
            Directory.Move(source,backup);return new ClientResetResult{changed=true,backup=backup};
        }
    }
}
}
