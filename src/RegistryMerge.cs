using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using System.Security.Cryptography;
namespace SCUMLab {
public sealed class TagValue { public string storageType,value; }
public sealed class DeltaEntry { public string objectPath,packagePath,assetClass,packageName,assetName;public Dictionary<string,TagValue> tags;public int[] chunks;public uint packageFlags; }
public sealed class RegistryDelta { public string status;public DeltaEntry[] entries; }
public static class RegistryMerge {
    static void Require(bool b,string message){if(!b)throw new InvalidDataException(message);}
    static string Str(BinaryReader r){int n=r.ReadInt32();Require(n!=Int32.MinValue&&Math.Abs(n)<1000000,"Registry string bound");return n==0?"":(n<0?Encoding.Unicode:Encoding.UTF8).GetString(r.ReadBytes(n<0?-2*n:n)).TrimEnd('\0');}
    static void Skip(BinaryReader r,long n){Require(n>=0&&r.BaseStream.Position+n<=r.BaseStream.Length,"Registry bounds");r.BaseStream.Position+=n;}
    static string Name(BinaryReader r,List<string> names){uint id=r.ReadUInt32();uint n=(id&0x80000000)!=0?r.ReadUInt32():0;id&=0x7fffffff;Require(id<names.Count,"Registry name id");return names[(int)id]+(n>0?"_"+(n-1):"");}
    static void Copy(BinaryWriter w,byte[] b,long start,long end){Require(start>=0&&end>=start&&end<=b.Length,"Copy bounds");w.Write(b,(int)start,(int)(end-start));}
    static byte[] Bytes(Action<BinaryWriter> write){using(MemoryStream m=new MemoryStream()){using(BinaryWriter w=new BinaryWriter(m,Encoding.UTF8,true))write(w);return m.ToArray();}}
    public static byte[] Merge(byte[] source,RegistryDelta delta) {
        Require(delta!=null&&delta.entries!=null&&delta.entries.Length>0&&delta.entries.Length<256,"Invalid registry delta");
        List<string> names=new List<string>();long hashes,headers,strings,storeStart,assetsStart,assetsEnd;uint magic;int[] counts=new int[11];long[] starts=new long[11],ends=new long[11];byte[] textPrefix=null;int assetCount;HashSet<string> existing=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using(BinaryReader r=new BinaryReader(new MemoryStream(source))){
            byte[] expected={0xe7,0x9e,0x7f,0x71,0x3a,0x49,0xb0,0xe9,0x32,0x91,0xb3,0x88,0x07,0x81,0x38,0x1b};Require(Convert.ToBase64String(r.ReadBytes(16))==Convert.ToBase64String(expected)&&r.ReadUInt32()==8,"Only registry v8 supported");
            int count=r.ReadInt32(),stringBytes=r.ReadInt32();Require(count>0&&count<2000000&&stringBytes>=0,"Registry name count");Skip(r,8);hashes=r.BaseStream.Position;Skip(r,8L*count);headers=r.BaseStream.Position;byte[] lengths=r.ReadBytes(2*count);strings=r.BaseStream.Position;
            for(int i=0;i<count;i++){int h=lengths[2*i],length=((h&127)<<8)|lengths[2*i+1];names.Add(((h&128)!=0?Encoding.Unicode:Encoding.UTF8).GetString(r.ReadBytes(length*((h&128)!=0?2:1))));}Require(r.BaseStream.Position-strings==stringBytes,"Name byte count");storeStart=r.BaseStream.Position;
            magic=r.ReadUInt32();Require(magic==0x12345678||magic==0x12345679,"Registry tag store");for(int i=0;i<11;i++){counts[i]=r.ReadInt32();Require(counts[i]>=0&&counts[i]<10000000,"Tag count");}
            Action<int,Action> block=(i,read)=>{starts[i]=r.BaseStream.Position;read();ends[i]=r.BaseStream.Position;};
            Action texts=()=>{for(int i=0;i<counts[4];i++)Str(r);};
            if(magic==0x12345679){textPrefix=r.ReadBytes(4);block(4,texts);}
            block(0,()=>Skip(r,4L*counts[0]));block(1,()=>{for(int i=0;i<counts[1];i++)Name(r,names);});
            block(2,()=>Skip(r,12L*counts[2]));block(3,()=>{for(int i=0;i<counts[3]*3;i++)Name(r,names);});if(magic==0x12345678)block(4,texts);
            block(5,()=>Skip(r,4L*counts[5]));block(6,()=>Skip(r,4L*counts[6]));block(7,()=>Skip(r,counts[7]));block(8,()=>Skip(r,2L*counts[8]));block(9,()=>Skip(r,8L*counts[9]));block(10,()=>{for(int i=0;i<counts[10];i++){Name(r,names);r.ReadUInt32();}});
            Require(r.ReadUInt32()==0x87654321,"Tag store end");assetCount=r.ReadInt32();Require(assetCount>=0&&assetCount<2000000,"Asset count");assetsStart=r.BaseStream.Position;
            for(int i=0;i<assetCount;i++){existing.Add(Name(r,names));for(int k=0;k<4;k++)Name(r,names);Skip(r,8);int bundles=r.ReadInt32();Require(bundles>=0&&bundles<100000,"Bundle count");for(int j=0;j<bundles;j++){Name(r,names);int n=r.ReadInt32();Require(n>=0&&n<100000,"Bundle size");for(int k=0;k<n;k++){Name(r,names);Str(r);}}int chunks=r.ReadInt32();Require(chunks>=0&&chunks<100000,"Chunk count");Skip(r,4L*chunks+4);}
            assetsEnd=r.BaseStream.Position;Require(r.ReadInt64()==4&&r.ReadInt32()==0&&r.ReadInt32()==0&&r.BaseStream.Position==source.Length,"Nonempty registry graphs/package data are unsupported");
        }
        int originalNames=names.Count;Dictionary<string,int> ids=new Dictionary<string,int>(StringComparer.Ordinal);for(int i=0;i<names.Count;i++)ids[names[i]]=i;
        Func<string,int> id=s=>{int v;if(!ids.TryGetValue(s,out v)){Require(!String.IsNullOrEmpty(s)&&s.Length<32768,"New name size");foreach(char c in s)Require(c>=32&&c<=126,"New names must be ASCII");v=names.Count;names.Add(s);ids.Add(s,v);}return v;};
        List<byte[]>[] extra=new List<byte[]>[11];for(int i=0;i<11;i++)extra[i]=new List<byte[]>();
        Func<int,byte[],int> add=(i,b)=>{int v=counts[i]++;extra[i].Add(b);return v;};
        Func<TagValue,uint> value=t=>{
            Require(t!=null&&t.value!=null,"Missing tag value");int type,index;
            if(t.storageType=="NumberlessName"){type=2;index=add(0,Bytes(w=>w.Write(id(t.value))));}
            else if(t.storageType=="NumberlessExportPath"){
                var m=System.Text.RegularExpressions.Regex.Match(t.value,"^([A-Za-z0-9_]+)'(/[^']+)\\.([A-Za-z0-9_]+)'$");Require(m.Success,"Export path syntax");type=4;index=add(2,Bytes(w=>{w.Write(id(m.Groups[1].Value));w.Write(id(m.Groups[3].Value));w.Write(id(m.Groups[2].Value));}));
            }else if(t.storageType=="AnsiString"){foreach(char c in t.value)Require(c>=32&&c<=126,"Tag ASCII only");type=0;index=add(5,Bytes(w=>w.Write(counts[7])));byte[] b=Encoding.ASCII.GetBytes(t.value+"\0");extra[7].Add(b);counts[7]+=b.Length;}
            else throw new InvalidDataException("Unsupported delta tag storage");return (uint)(index*8+type);
        };
        List<byte[]> records=new List<byte[]>();HashSet<string> added=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(DeltaEntry e in delta.entries){
            Require(e.objectPath!=null&&e.objectPath.StartsWith("/Game/",StringComparison.Ordinal)&&e.objectPath.Contains("/SCUMLab/"),"Foreign delta namespace");Require(!existing.Contains(e.objectPath)&&added.Add(e.objectPath),"Existing/duplicate delta path");
            int dot=e.objectPath.LastIndexOf('.');Require(dot>0,"Object path");string package=e.objectPath.Substring(0,dot),asset=e.objectPath.Substring(dot+1),folder=package.Substring(0,package.LastIndexOf('/'));Require(e.packageName==package&&e.assetName==asset&&e.packagePath==folder,"Delta path mismatch");Require(e.tags!=null&&e.tags.Count<65536,"Tags missing");int begin=counts[9];
            foreach(var pair in e.tags)add(9,Bytes(w=>{w.Write(id(pair.Key));w.Write(value(pair.Value));}));
            records.Add(Bytes(w=>{w.Write(id(e.objectPath));w.Write(id(folder));w.Write(id(e.assetClass));w.Write(id(package));w.Write(id(asset));w.Write((1UL<<63)|((ulong)e.tags.Count<<32)|(uint)begin);w.Write(0);int[] chunks=e.chunks??new int[0];w.Write(chunks.Length);foreach(int c in chunks)w.Write(c);w.Write(e.packageFlags);}));
        }
        return Bytes(w=>{
            Copy(w,source,0,20);w.Write(names.Count);int bytes=checked((int)(storeStart-strings));for(int i=originalNames;i<names.Count;i++)bytes+=names[i].Length;w.Write(bytes);w.Write(0UL); // UE recomputes hashes for algorithm version 0.
            Copy(w,source,hashes,headers);w.Write(new byte[8*(names.Count-originalNames)]);Copy(w,source,headers,strings);
            for(int i=originalNames;i<names.Count;i++){w.Write((byte)(names[i].Length>>8));w.Write((byte)names[i].Length);}Copy(w,source,strings,storeStart);for(int i=originalNames;i<names.Count;i++)w.Write(Encoding.ASCII.GetBytes(names[i]));
            w.Write(magic);foreach(int n in counts)w.Write(n);Action<int> block=i=>{Copy(w,source,starts[i],ends[i]);foreach(byte[] b in extra[i])w.Write(b);};
            if(magic==0x12345679){w.Write(textPrefix);block(4);}for(int i=0;i<4;i++)block(i);if(magic==0x12345678)block(4);for(int i=5;i<11;i++)block(i);w.Write(0x87654321U);w.Write(assetCount+records.Count);Copy(w,source,assetsStart,assetsEnd);foreach(byte[] b in records)w.Write(b);Copy(w,source,assetsEnd,source.Length);
        });
    }
}
public static class RegistryPak {
    static void FString(BinaryWriter w,string s){byte[] b=Encoding.ASCII.GetBytes(s+"\0");w.Write(b.Length);w.Write(b);}
    static byte[] Bytes(Action<BinaryWriter> write){using(MemoryStream m=new MemoryStream()){using(BinaryWriter w=new BinaryWriter(m,Encoding.UTF8,true))write(w);return m.ToArray();}}
    static byte[] Hash(byte[] b){using(SHA1 h=SHA1.Create())return h.ComputeHash(b);}
    public static void Write(string file,byte[] registry) {
        byte[] payloadHash=Hash(registry);
        byte[] directory=Bytes(w=>{w.Write(1);w.Write(0);w.Write(1);FString(w,"AssetRegistry.bin");w.Write(0);});
        long directoryOffset=53L+registry.Length;
        byte[] primary=Bytes(w=>{FString(w,"../../../SCUM/");w.Write(1);w.Write(0UL);w.Write(0);w.Write(1);w.Write(directoryOffset);w.Write((long)directory.Length);w.Write(Hash(directory));w.Write(12);w.Write(0xc0000000U);w.Write(0U);w.Write((uint)registry.Length);w.Write(0);});
        using(FileStream f=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(BinaryWriter w=new BinaryWriter(f)){
            w.Write(0L);w.Write((long)registry.Length);w.Write((long)registry.Length);w.Write(0U);w.Write(payloadHash);w.Write((byte)0);w.Write(0U);w.Write(registry);w.Write(directory);long offset=f.Position;w.Write(primary);w.Write(new byte[16]);w.Write((byte)0);w.Write(0x5a6f12e1U);w.Write(11);w.Write(offset);w.Write((long)primary.Length);w.Write(Hash(primary));w.Write(new byte[160]);f.Flush(true);
        }
    }
}
}
