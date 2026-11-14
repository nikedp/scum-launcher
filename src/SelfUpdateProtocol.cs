using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
namespace SCUMLab {
public sealed class LauncherUpdateJob {
    public string target,staged,oldSHA256,newSHA256,expectedVersion,token;
    public int parentPid;public long parentStartedUtcTicks;public bool restart=true;
}
public sealed class LauncherUpdateResult { public bool ok,restored;public string message,backup,checkedUtc; }
public static class LauncherUpdateTransaction {
    static void Require(bool value,string message){if(!value)throw new InvalidDataException(message);}
    public static string Hash(string file){using(FileStream f=File.OpenRead(file))using(SHA256 h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();}
    static bool Same(string a,string b){return String.Equals(a,b,StringComparison.OrdinalIgnoreCase);}
    public static void Validate(LauncherUpdateJob job){
        Require(job!=null&&Regex.IsMatch(job.token??"","^[a-f0-9]{32}$"),"Некорректное задание обновления.");
        Require(Regex.IsMatch(job.oldSHA256??"","^[a-fA-F0-9]{64}$")&&Regex.IsMatch(job.newSHA256??"","^[a-fA-F0-9]{64}$"),"Не указаны контрольные суммы обновления.");
        Require(Path.IsPathRooted(job.target)&&Path.IsPathRooted(job.staged)&&!Same(Path.GetFullPath(job.target),Path.GetFullPath(job.staged)),"Некорректный путь обновления.");
        Require(File.Exists(job.target)&&File.Exists(job.staged),"Файл лаунчера не найден.");
        Require((File.GetAttributes(job.target)&FileAttributes.ReparsePoint)==0&&(File.GetAttributes(job.staged)&FileAttributes.ReparsePoint)==0,"Обновление ссылок на EXE не поддерживается.");
        Require(Same(Hash(job.target),job.oldSHA256),"Лаунчер изменился после подготовки обновления. Повторите попытку.");
        Require(Same(Hash(job.staged),job.newSHA256),"Новый EXE повреждён.");
        AssemblyName assembly=AssemblyName.GetAssemblyName(job.staged);Version version;
        Require(assembly.Name=="SCUMLabLauncher"&&Version.TryParse(job.expectedVersion,out version)&&assembly.Version.Major==version.Major&&assembly.Version.Minor==version.Minor&&assembly.Version.Build==version.Build,"Версия нового EXE не совпала с релизом.");
    }
    public static string Apply(LauncherUpdateJob job,Action<string> restart){
        Validate(job);string next=job.target+".update-"+job.token,backup=job.target+".previous-"+job.token+".bak";bool replaced=false;
        Require(!File.Exists(next)&&!File.Exists(backup),"Временные файлы обновления уже существуют.");
        try{
            File.Copy(job.staged,next);Require(Same(Hash(next),job.newSHA256),"Копия нового EXE повреждена.");
            for(int attempt=0;;attempt++){
                Require(Same(Hash(job.target),job.oldSHA256),"EXE был изменён другим процессом.");
                try{File.Replace(next,job.target,backup);replaced=true;break;}
                catch(IOException){if(attempt>=39)throw;Thread.Sleep(200);}
            }
            Require(Same(Hash(job.target),job.newSHA256)&&Same(Hash(backup),job.oldSHA256),"Контрольная сумма после замены не совпала.");
            if(restart!=null)restart(job.target);return backup;
        }catch{
            if(replaced&&File.Exists(backup)&&Same(Hash(backup),job.oldSHA256)&&File.Exists(job.target)&&Same(Hash(job.target),job.newSHA256))File.Replace(backup,job.target,job.target+".failed-"+job.token+".bak");
            throw;
        }finally{if(File.Exists(next))File.Delete(next);}
    }
}
}
