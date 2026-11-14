// Original read-only helper for locally installed UE4 PAKs, Windows/.NET 4+.
// Format references: Epic UE4.27 ModuleManager.h and IPlatformFilePak.cpp.
using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
namespace SCUMLab {
public sealed class RegistryExtraction {
    public string Pak, Member, File, SHA256;
    public long Bytes;
}
public sealed class ExtractionResult {
    public int PakCount, VerifiedPakCount;
    public string ExecutableSHA256;
    public List<string> Errors = new List<string>();
    public List<RegistryExtraction> Registries = new List<RegistryExtraction>();
}
public static class RegistryReader {
    sealed class Footer { public long Size, Offset, Length, FooterOffset; public bool Encrypted; public byte[] Hash; public string[] Methods; }
    sealed class Store { public int At, Base, Disp; public byte[] Bytes; }
    sealed class Entry { public long Offset, Size, RawSize; public uint Compression; public bool Encrypted; public byte[] Hash; public List<long[]> Blocks = new List<long[]>(); }
    static byte[] Slice(byte[] b,int at,int n) { byte[] v=new byte[n];Buffer.BlockCopy(b,at,v,0,n);return v; }
    static byte[] ReadAt(FileStream f,long at,long n) {
        if(at<0||n<0||n>134217728||at+n>f.Length)throw new InvalidDataException("Bounded read outside file");
        byte[] b=new byte[(int)n];f.Position=at;int p=0;while(p<b.Length){int got=f.Read(b,p,b.Length-p);if(got==0)throw new EndOfStreamException();p+=got;}return b;
    }
    static byte[] SHA1Bytes(byte[] b) { using(SHA1 h=SHA1.Create())return h.ComputeHash(b); }
    static string SHA256Text(byte[] b) { using(SHA256 h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(b)).Replace("-","").ToLowerInvariant(); }
    static bool Equal(byte[] a,byte[] b) { if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true; }
    static byte[] Decrypt(byte[] b,byte[] key) {
        if(b.Length%16!=0)throw new InvalidDataException("AES block alignment");
        using(Aes a=Aes.Create()){a.Key=key;a.Mode=CipherMode.ECB;a.Padding=PaddingMode.None;using(ICryptoTransform d=a.CreateDecryptor())return d.TransformFinalBlock(b,0,b.Length);}
    }
    static Footer GetFooter(FileStream f) {
        long n=Math.Min(f.Length,256);byte[] b=ReadAt(f,f.Length-n,n);int m=-1;
        for(int i=b.Length-44;i>=17;i--)if(BitConverter.ToUInt32(b,i)==0x5a6f12e1){m=i;break;}
        if(m<17||m+204>b.Length)throw new InvalidDataException("PAK footer absent");
        uint version=BitConverter.ToUInt32(b,m+4);if(version!=10&&version!=11)throw new InvalidDataException("Only PAK v10/v11 supported");
        Footer x=new Footer{Size=f.Length,Offset=BitConverter.ToInt64(b,m+8),Length=BitConverter.ToInt64(b,m+16),Encrypted=b[m-1]!=0,Hash=Slice(b,m+24,20),FooterOffset=f.Length-n+m-17,Methods=new string[5]};
        if(x.Offset<0||x.Length<1||x.Offset+x.Length>x.FooterOffset)throw new InvalidDataException("Invalid primary index bounds");
        for(int i=0;i<5;i++)x.Methods[i]=Encoding.ASCII.GetString(b,m+44+i*32,32).Split('\0')[0];return x;
    }
    static Store Immediate(byte[] b,int at,int end) {
        int p=at,rex=0;if(b[p]>=0x40&&b[p]<=0x4f)rex=b[p++];if(b[p++]!=0xc7)return null;
        int mr=b[p++],mod=mr>>6,rm=mr&7;if((mr&56)!=0||mod==3||(rex&8)!=0)return null;
        int basis=rm+(((rex&1)!=0)?8:0),disp=0;
        if(rm==4){int sib=b[p++];if((sib&56)!=32)return null;basis=(sib&7)+(((rex&1)!=0)?8:0);if(mod==0&&(sib&7)==5)return null;}
        else if(mod==0&&rm==5)return null;
        if(mod==1)disp=(sbyte)b[p++];else if(mod==2){disp=BitConverter.ToInt32(b,p);p+=4;}
        if(p+4>end)return null;return new Store{At=at,Base=basis,Disp=disp,Bytes=Slice(b,p,4)};
    }
    static byte[] FindKey(byte[] exe,byte[] encrypted,byte[] expected) {
        int pe=BitConverter.ToInt32(exe,0x3c);if(exe[0]!=77||exe[1]!=90||BitConverter.ToUInt32(exe,pe)!=0x4550||BitConverter.ToUInt16(exe,pe+4)!=0x8664)throw new InvalidDataException("Expected x64 PE");
        int sections=pe+24+BitConverter.ToUInt16(exe,pe+20),count=BitConverter.ToUInt16(exe,pe+6);List<Store> stores=new List<Store>();
        for(int s=0;s<count;s++){int h=sections+40*s,start=BitConverter.ToInt32(exe,h+20),size=BitConverter.ToInt32(exe,h+16);uint flags=BitConverter.ToUInt32(exe,h+36);if(start<0||size<0||(long)start+size>exe.Length)throw new InvalidDataException("PE bounds");if((flags&0x20000000)==0)continue;
            for(int at=start;at<start+size-12;at++){if(exe[at]!=0xc7&&!((exe[at]&0xf0)==0x40&&exe[at+1]==0xc7))continue;Store v=Immediate(exe,at,start+size);if(v!=null)stores.Add(v);}}
        HashSet<string> tested=new HashSet<string>();
        for(int i=0;i<stores.Count;i++){Store first=stores[i];Dictionary<int,Store> nearby=new Dictionary<int,Store>();for(int j=i;j<Math.Min(i+24,stores.Count);j++){Store v=stores[j];if(v.At-first.At<160&&v.Base==first.Base)nearby[v.Disp]=v;}
            byte[] key=new byte[32];bool complete=true;for(int w=0;w<8;w++){Store v;if(!nearby.TryGetValue(first.Disp+4*w,out v)){complete=false;break;}Buffer.BlockCopy(v.Bytes,0,key,w*4,4);}if(!complete)continue;
            if(new HashSet<byte>(key).Count<16||!tested.Add(Convert.ToBase64String(key)))continue;
            if(Equal(SHA1Bytes(Decrypt(encrypted,key)),expected))return key;}
        throw new InvalidDataException("No statically verified local key found; game files were not changed");
    }
    static string FString(BinaryReader r) { int n=r.ReadInt32();if(n==0)return "";if(n==Int32.MinValue||Math.Abs(n)>1000000)throw new InvalidDataException("String bound");int length=n<0?-n*2:n;byte[] b=r.ReadBytes(length);if(b.Length!=length)throw new EndOfStreamException();return (n<0?Encoding.Unicode:Encoding.UTF8).GetString(b).TrimEnd('\0'); }
    static byte[] IndexRegion(FileStream f,Footer footer,long offset,long length,byte[] hash,byte[] key) {
        if(offset+length>footer.FooterOffset)throw new InvalidDataException("Index bounds");byte[] b=ReadAt(f,offset,length);if(footer.Encrypted)b=Decrypt(b,key);if(!Equal(SHA1Bytes(b),hash))throw new InvalidDataException("Index SHA1 mismatch");return b;
    }
    static Entry RawEntry(BinaryReader r) {
        Entry e=new Entry{Offset=r.ReadInt64(),Size=r.ReadInt64(),RawSize=r.ReadInt64(),Compression=r.ReadUInt32(),Hash=r.ReadBytes(20)};
        if(e.Compression!=0){int n=r.ReadInt32();if(n<0||n>65535)throw new InvalidDataException("Block count");for(int i=0;i<n;i++)e.Blocks.Add(new long[]{r.ReadInt64(),r.ReadInt64()});}
        byte flags=r.ReadByte();e.Encrypted=(flags&1)!=0;if((flags&2)!=0)throw new InvalidDataException("Deleted payload");r.ReadUInt32();return e;
    }
    static long Variable(BinaryReader r,uint bits,int bit) { return ((bits>>bit)&1)!=0?r.ReadUInt32():r.ReadInt64(); }
    static Entry Encoded(byte[] b,int at) {
        using(BinaryReader r=new BinaryReader(new MemoryStream(b))){r.BaseStream.Position=at;uint bits=r.ReadUInt32();if((bits&63)==63)r.ReadUInt32();Entry e=new Entry();e.Offset=Variable(r,bits,31);e.RawSize=Variable(r,bits,30);e.Compression=(bits>>23)&63;e.Size=e.Compression!=0?Variable(r,bits,29):e.RawSize;e.Encrypted=(bits&(1<<22))!=0;return e;}
    }
    static byte[] Payload(FileStream f,Footer footer,Entry expected,byte[] key) {
        byte[] first=ReadAt(f,expected.Offset,52);uint compression=BitConverter.ToUInt32(first,24);int blocks=compression!=0?BitConverter.ToInt32(first,48):0;if(blocks<0||blocks>65535)throw new InvalidDataException("Payload blocks");
        int headerSize=53+(compression!=0?4+16*blocks:0);Entry e;using(BinaryReader r=new BinaryReader(new MemoryStream(ReadAt(f,expected.Offset,headerSize))))e=RawEntry(r);
        if(e.Size!=expected.Size||e.RawSize!=expected.RawSize||e.Compression!=expected.Compression||e.Encrypted!=expected.Encrypted)throw new InvalidDataException("Payload/index mismatch");
        long storedSize=e.Encrypted&&e.Compression==0?(e.Size+15)/16*16:e.Size;if(expected.Offset+headerSize+storedSize>footer.Offset)throw new InvalidDataException("Payload bounds");
        byte[] stored=ReadAt(f,expected.Offset+headerSize,storedSize);byte[] actual;using(SHA1 h=SHA1.Create())actual=h.ComputeHash(stored,0,(int)e.Size);if(!Equal(actual,e.Hash))throw new InvalidDataException("Payload SHA1 mismatch");
        if(e.Compression==0){byte[] raw=e.Encrypted?Decrypt(stored,key):stored;return Slice(raw,0,(int)e.RawSize);}
        if(e.Compression>footer.Methods.Length||footer.Methods[e.Compression-1]!="Zlib")throw new InvalidDataException("Unsupported payload compression");
        using(MemoryStream output=new MemoryStream()){
            foreach(long[] block in e.Blocks){int length=checked((int)(block[1]-block[0])),at=checked((int)(block[0]-headerSize)),storedLength=e.Encrypted?(length+15)/16*16:length;
                if(length<6||at<0||(long)at+storedLength>stored.Length)throw new InvalidDataException("Compressed block bounds");byte[] data=Slice(stored,at,storedLength);if(e.Encrypted)data=Decrypt(data,key);
                if((data[0]&15)!=8||((data[0]<<8)+data[1])%31!=0||(data[1]&32)!=0)throw new InvalidDataException("Unsupported Zlib header");
                using(MemoryStream compressed=new MemoryStream(data,2,length-6))using(DeflateStream deflate=new DeflateStream(compressed,CompressionMode.Decompress)){
                    byte[] chunk=new byte[65536];int got;while((got=deflate.Read(chunk,0,chunk.Length))>0){output.Write(chunk,0,got);if(output.Length>e.RawSize)throw new InvalidDataException("Decompressed size exceeds index");}}
            }
            if(output.Length!=e.RawSize)throw new InvalidDataException("Decompressed size mismatch");return output.ToArray();
        }
    }
    static void ExtractPak(string file,byte[] key,string output,ExtractionResult result) {
        using(FileStream f=File.Open(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){
            Footer footer=GetFooter(f);byte[] primary=IndexRegion(f,footer,footer.Offset,footer.Length,footer.Hash,key),encoded,directory=null;Entry[] unencoded;string mount;int count;
            using(BinaryReader r=new BinaryReader(new MemoryStream(primary))){mount=FString(r);count=r.ReadInt32();r.ReadUInt64();
                for(int i=0;i<2;i++)if(r.ReadInt32()!=0){long offset=r.ReadInt64(),length=r.ReadInt64();byte[] hash=r.ReadBytes(20);byte[] bytes=IndexRegion(f,footer,offset,length,hash,key);if(i==1)directory=bytes;}
                int n=r.ReadInt32();if(n<0||n>134217728)throw new InvalidDataException("Encoded table size");encoded=r.ReadBytes(n);if(encoded.Length!=n)throw new EndOfStreamException();int rawCount=r.ReadInt32();if(rawCount<0||rawCount>1000000)throw new InvalidDataException("Raw entry count");unencoded=new Entry[rawCount];for(int i=0;i<rawCount;i++)unencoded[i]=RawEntry(r);}
            if(directory==null)throw new InvalidDataException("Full directory index absent");int seen=0;
            using(BinaryReader r=new BinaryReader(new MemoryStream(directory))){int dirs=r.ReadInt32();if(dirs<0||dirs>1000000)throw new InvalidDataException("Directory count");
                for(int i=0;i<dirs;i++){string folder=FString(r);int n=r.ReadInt32();if(n<0||n>1000000)throw new InvalidDataException("File count");for(int j=0;j<n;j++){
                    string name=FString(r);int location=r.ReadInt32();if(location==Int32.MinValue)continue;seen++;if(!String.Equals(name,"AssetRegistry.bin",StringComparison.OrdinalIgnoreCase))continue;
                    Entry e=location<0?unencoded[-location-1]:Encoded(encoded,location);byte[] data=Payload(f,footer,e,key);
                    if(data.Length<20||BitConverter.ToUInt32(data,16)!=8)throw new InvalidDataException("Expected registry v8");
                    byte[] magic={0xe7,0x9e,0x7f,0x71,0x3a,0x49,0xb0,0xe9,0x32,0x91,0xb3,0x88,0x07,0x81,0x38,0x1b};if(!Equal(Slice(data,0,16),magic))throw new InvalidDataException("Registry GUID mismatch");
                    string destination="registry-"+result.Registries.Count+"-AssetRegistry.bin";File.WriteAllBytes(Path.Combine(output,destination),data);result.Registries.Add(new RegistryExtraction{Pak=Path.GetFileName(file),Member=mount+folder+name,File=destination,SHA256=SHA256Text(data),Bytes=data.Length});
                }}if(seen!=count||directory.Length-r.BaseStream.Position>15)throw new InvalidDataException("Directory extent mismatch");}
        }
    }
    public static ExtractionResult Extract(string exe,string pakDirectory,string output) {
        string[] files=Array.FindAll(Directory.GetFiles(pakDirectory,"*.pak"),f=>!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(f),"^pakchunk[0-9]+-SCUMLab[A-Za-z0-9_-]*_P\\.pak$",System.Text.RegularExpressions.RegexOptions.IgnoreCase));if(files.Length==0)throw new InvalidDataException("No stock PAKs");
        string smallest=files[0];foreach(string file in files)if(new FileInfo(file).Length<new FileInfo(smallest).Length)smallest=file;
        byte[] executable=File.ReadAllBytes(exe),key=null;using(FileStream f=File.OpenRead(smallest)){Footer footer=GetFooter(f);if(!footer.Encrypted)throw new InvalidDataException("Smallest PAK has no encrypted index; this probe expects encrypted local game PAKs");key=FindKey(executable,ReadAt(f,footer.Offset,footer.Length),footer.Hash);}
        ExtractionResult result=new ExtractionResult{PakCount=files.Length,ExecutableSHA256=SHA256Text(executable)};Directory.CreateDirectory(output);Array.Sort(files,StringComparer.OrdinalIgnoreCase);
        foreach(string file in files){try{ExtractPak(file,key,output,result);result.VerifiedPakCount++;}catch(Exception e){result.Errors.Add(Path.GetFileName(file)+": "+e.Message);}}
        Array.Clear(key,0,key.Length);return result;
    }
}
}
