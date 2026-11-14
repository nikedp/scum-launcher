using System;
using System.IO;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security;
using System.Text.RegularExpressions;
using Microsoft.Win32;
namespace SCUMLab {
public sealed class GameSearchResult { public string[] paths;public bool limited; }
public static class GameDiscovery {
    static bool AccessError(Exception e){return e is IOException||e is UnauthorizedAccessException||e is SecurityException||e is ArgumentException||e is NotSupportedException;}
    static string Read(string file){try{if(File.Exists(file)&&new FileInfo(file).Length<=2*1024*1024)return File.ReadAllText(file);}catch(Exception e){if(!AccessError(e))throw;}return "";}
    static bool Add(HashSet<string> paths,string value){try{if(String.IsNullOrWhiteSpace(value)||!Path.IsPathRooted(value))return false;return paths.Add(Path.GetFullPath(value));}catch(Exception e){if(!AccessError(e))throw;return false;}}
    public static bool IsClient(string root){try{return Directory.Exists(Path.Combine(root,"SCUM","Content","Paks"))&&File.Exists(Path.Combine(root,"SCUM","Binaries","Win64","SCUM.exe"));}catch(Exception e){if(!AccessError(e))throw;return false;}}
    internal static string[] FromLibraries(IEnumerable<string> roots){
        HashSet<string> libraries=new HashSet<string>(StringComparer.OrdinalIgnoreCase),found=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(string root in roots)Add(libraries,root);
        foreach(string library in new List<string>(libraries)){
            string vdf=Read(Path.Combine(library,"steamapps","libraryfolders.vdf"));
            foreach(Match m in Regex.Matches(vdf,"\"(?:path|[0-9]+)\"\\s+\"([^\"]+)\"",RegexOptions.IgnoreCase)){
                string value=m.Groups[1].Value.Replace("\\\\","\\");Add(libraries,value);
            }
        }
        foreach(string library in libraries){
            string manifest=Read(Path.Combine(library,"steamapps","appmanifest_513710.acf"));
            string dir=Regex.Match(manifest,"\"installdir\"\\s+\"([^\"]+)\"",RegexOptions.IgnoreCase).Groups[1].Value;
            // Steam installdir is a single child of common, never an arbitrary path.
            if(dir.Length>0&&dir!="."&&dir!=".."&&dir.IndexOfAny(Path.GetInvalidFileNameChars())<0){string root=Path.Combine(library,"steamapps","common",dir);if(IsClient(root))Add(found,root);}
            string usual=Path.Combine(library,"steamapps","common","SCUM");if(IsClient(usual))Add(found,usual);
        }
        string[] result=new List<string>(found).ToArray();Array.Sort(result,StringComparer.OrdinalIgnoreCase);return result;
    }
    static List<string> SteamRoots(){
        List<string> roots=new List<string>();
        foreach(RegistryKey hive in new[]{Registry.CurrentUser,Registry.LocalMachine})foreach(string key in new[]{@"Software\Valve\Steam",@"Software\WOW6432Node\Valve\Steam"}){
            try{using(RegistryKey k=hive.OpenSubKey(key))if(k!=null)foreach(string name in new[]{"SteamPath","InstallPath"}){string value=k.GetValue(name) as string;if(!String.IsNullOrWhiteSpace(value))roots.Add(value);}}catch(Exception e){if(!AccessError(e))throw;}
        }
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),"Steam"));
        roots.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"Steam"));
        foreach(string drive in FixedDrives())foreach(string folder in new[]{"Steam","SteamLibrary",@"Games\Steam",@"Games\SteamLibrary",@"Program Files (x86)\Steam",@"Program Files\Steam"})roots.Add(Path.Combine(drive,folder));
        return roots;
    }
    static string[] FixedDrives(){List<string> drives=new List<string>();foreach(DriveInfo drive in DriveInfo.GetDrives())try{if(drive.DriveType==DriveType.Fixed&&drive.IsReady)drives.Add(drive.RootDirectory.FullName);}catch(Exception e){if(!AccessError(e))throw;}return drives.ToArray();}
    sealed class Node {public string path;public int depth;}
    internal static GameSearchResult Scan(IEnumerable<string> roots,int maxDirectories,int maxDepth,TimeSpan budget){
        HashSet<string> found=new HashSet<string>(StringComparer.OrdinalIgnoreCase),visited=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> skip=new HashSet<string>(new[]{"Windows","ProgramData","$Recycle.Bin","System Volume Information","node_modules",".git"},StringComparer.OrdinalIgnoreCase);
        Queue<Node> queue=new Queue<Node>();foreach(string root in roots)queue.Enqueue(new Node{path=root});Stopwatch clock=Stopwatch.StartNew();int count=0;bool limited=false;
        while(queue.Count>0){
            if(count>=maxDirectories||clock.Elapsed>=budget){limited=true;break;}Node node=queue.Dequeue();
            try{
                if(!Add(visited,node.path)||(File.GetAttributes(node.path)&FileAttributes.ReparsePoint)!=0)continue;count++;
                if(IsClient(node.path)){Add(found,node.path);continue;}
                if(Path.GetFileName(node.path).Equals("steamapps",StringComparison.OrdinalIgnoreCase))foreach(string path in FromLibraries(new[]{Directory.GetParent(node.path).FullName}))Add(found,path);
                if(node.depth>=maxDepth){limited=true;continue;}
                foreach(string child in Directory.EnumerateDirectories(node.path)){if(clock.Elapsed>=budget||queue.Count>=maxDirectories){limited=true;break;}if(!skip.Contains(Path.GetFileName(child)))queue.Enqueue(new Node{path=child,depth=node.depth+1});}
            }catch(Exception e){if(!AccessError(e))throw;}
        }
        string[] paths=new List<string>(found).ToArray();Array.Sort(paths,StringComparer.OrdinalIgnoreCase);return new GameSearchResult{paths=paths,limited=limited};
    }
    public static GameSearchResult Find(bool extended){
        HashSet<string> found=new HashSet<string>(FromLibraries(SteamRoots()),StringComparer.OrdinalIgnoreCase);bool limited=false;
        if(extended){GameSearchResult scan=Scan(FixedDrives(),12000,5,TimeSpan.FromSeconds(15));limited=scan.limited;foreach(string path in scan.paths)Add(found,path);}
        string[] paths=new List<string>(found).ToArray();Array.Sort(paths,StringComparer.OrdinalIgnoreCase);return new GameSearchResult{paths=paths,limited=limited};
    }
}
}
