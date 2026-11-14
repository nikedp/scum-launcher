using System;
using System.IO;
using System.Diagnostics;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
namespace SCUMLab {
internal static class UpdateHelperProgram {
    [STAThread]static int Main(string[] args){
        if(args.Length!=2||args[0]!="--apply")return 1;LauncherUpdateJob job=null;string resultPath=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[1])),"result.json");JavaScriptSerializer json=new JavaScriptSerializer();
        try{
            job=json.Deserialize<LauncherUpdateJob>(File.ReadAllText(args[1],Encoding.UTF8));
            if(job.parentPid<=0)throw new InvalidDataException("Не указан процесс обновляемого лаунчера.");
            Process parent=null;try{parent=Process.GetProcessById(job.parentPid);}catch(ArgumentException){}
            if(parent!=null)using(parent){if(parent.StartTime.ToUniversalTime().Ticks==job.parentStartedUtcTicks&&!parent.WaitForExit(60000))throw new IOException("Лаунчер ещё работает. Закройте его и повторите обновление.");}
            Action<string> restart=job.restart?(Action<string>)(path=>Process.Start(new ProcessStartInfo(path){WorkingDirectory=Path.GetDirectoryName(path),UseShellExecute=false})):null;
            string backup=LauncherUpdateTransaction.Apply(job,restart);
            File.WriteAllText(resultPath,json.Serialize(new LauncherUpdateResult{ok=true,message="Лаунчер обновлён и запущен.",backup=backup,checkedUtc=DateTime.UtcNow.ToString("o")}),new UTF8Encoding(false));return 0;
        }catch(Exception ex){
            bool restored=false;if(job!=null&&File.Exists(job.target))try{restored=String.Equals(LauncherUpdateTransaction.Hash(job.target),job.oldSHA256,StringComparison.OrdinalIgnoreCase);}catch{}
            string message="Не удалось обновить лаунчер. "+(restored?"Предыдущий EXE сохранён. ":"")+ex.Message;
            try{File.WriteAllText(resultPath,json.Serialize(new LauncherUpdateResult{ok=false,restored=restored,message=message,checkedUtc=DateTime.UtcNow.ToString("o")}),new UTF8Encoding(false));}catch{}
            if(job==null||job.restart)MessageBox.Show(message,"Обновление SCUM Lab",MessageBoxButtons.OK,MessageBoxIcon.Warning);
            if(restored&&job.restart)try{Process.Start(new ProcessStartInfo(job.target){WorkingDirectory=Path.GetDirectoryName(job.target),UseShellExecute=false});}catch{}
            return 1;
        }
    }
}
}
