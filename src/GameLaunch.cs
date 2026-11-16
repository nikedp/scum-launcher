using System;
using System.Diagnostics;
using System.IO;
namespace SCUMLab {
public static class GameLaunch {
    public static ProcessStartInfo ClientInfo(GameInstall game){
        Data.Require(game!=null&&!game.Server&&File.Exists(game.Exe),"Нужна установленная клиентская версия SCUM.");
        return new ProcessStartInfo(game.Exe,"-fileopenlog"){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(game.Exe)};
    }
    public static void Start(GameInstall game){
        Process[] steam=Process.GetProcessesByName("steam");
        try{Data.Require(steam.Length>0,"Сначала запустите Steam и войдите в свой аккаунт, затем нажмите «Запустить SCUM».");}finally{foreach(Process process in steam)process.Dispose();}
        Process.Start(ClientInfo(game));
    }
    // Ordinary launch through Steam (BattlEye, no -fileopenlog) for other servers; our PAKs must be parked first.
    public static void StartVanilla(){Process.Start(new ProcessStartInfo("steam://rungameid/513710"){UseShellExecute=true});}
}
}
