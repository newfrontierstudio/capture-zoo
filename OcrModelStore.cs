using System.IO.Compression;
using System.Security.Cryptography;
namespace CaptureZoo;
internal static class OcrModelStore
{
 internal sealed record Asset(string Package,long PackageBytes,string PackageHash,string Entry,string File,long FileBytes,string FileHash);
 internal static readonly Asset[] Assets=[
  new("sdcb.simdpaddleocr.models.textlineorientation",961574,"d925f70d542c4a2e829e423be24b3ba17508bde3215c7bdc0799d98e741303d8","lib/netstandard2.0/Sdcb.SimdPaddleOCR.Models.TextLineOrientation.dll","Sdcb.SimdPaddleOCR.Models.TextLineOrientation.dll",1026048,"c08ccb09b99e137989c6abfbae3dbf01c5b87709d350b745402a836e35cfab20"),
  new("sdcb.simdpaddleocr.models.chinesev6small",26114884,"e77523d9dcc682dd802d9271400dbbb2a6b3ed22bb7a9724e05bf5a75e585539","lib/netstandard2.0/Sdcb.SimdPaddleOCR.Models.ChineseV6Small.dll","Sdcb.SimdPaddleOCR.Models.ChineseV6Small.dll",31142400,"96e3fcfb4b6d3bd7365bc000003e1534abfe86605631e69e0f3793f33d8f0755")];
 internal static string? TestRoot;
 internal static string Root=>TestRoot??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CaptureZoo","Ocr","ppocr-v6-small-1.0.0");
 internal static Func<Asset,CancellationToken,Task<Stream>>? TestDownload;
 static readonly SemaphoreSlim Gate=new(1);
 static readonly HttpClient Http=new(){Timeout=TimeSpan.FromMinutes(10)};
 internal static long TotalBytes=>Assets.Sum(a=>a.PackageBytes);
 internal static bool Valid(string path,long bytes,string hash){if(!File.Exists(path)||new FileInfo(path).Length!=bytes)return false;using var file=File.OpenRead(path);return Convert.ToHexString(SHA256.HashData(file)).Equals(hash,StringComparison.OrdinalIgnoreCase);}
 internal static bool Ready()=>Assets.All(a=>Valid(Path.Combine(Root,a.File),a.FileBytes,a.FileHash));
 internal static async Task EnsureAsync(IProgress<(long Done,long Total)>? progress,CancellationToken token)
 {
  await Gate.WaitAsync(token).ConfigureAwait(false);
  try{Directory.CreateDirectory(Root);long finished=0;foreach(var a in Assets){token.ThrowIfCancellationRequested();string target=Path.Combine(Root,a.File);if(Valid(target,a.FileBytes,a.FileHash)){finished+=a.PackageBytes;progress?.Report((finished,TotalBytes));continue;}
   string archive=Path.Combine(Root,Guid.NewGuid()+".download"),stage=Path.Combine(Root,Guid.NewGuid()+".partial");
   try{await using(var input=TestDownload!=null?await TestDownload(a,token).ConfigureAwait(false):await Http.GetStreamAsync($"https://api.nuget.org/v3-flatcontainer/{a.Package}/1.0.0/{a.Package}.1.0.0.nupkg",token).ConfigureAwait(false))
    await using(var output=new FileStream(archive,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true)){var buffer=new byte[81920];long received=0;int read;while((read=await input.ReadAsync(buffer,token).ConfigureAwait(false))>0){received+=read;if(received>a.PackageBytes)throw new InvalidDataException("OCR download size mismatch");await output.WriteAsync(buffer.AsMemory(0,read),token).ConfigureAwait(false);progress?.Report((finished+received,TotalBytes));}}
    token.ThrowIfCancellationRequested();if(!Valid(archive,a.PackageBytes,a.PackageHash))throw new InvalidDataException("OCR package integrity check failed");
    using(var zip=ZipFile.OpenRead(archive)){var entry=zip.GetEntry(a.Entry)??throw new InvalidDataException("OCR model is missing");if(entry.Length!=a.FileBytes)throw new InvalidDataException("OCR model size mismatch");await using var input=entry.Open();await using var output=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None,81920,true);await input.CopyToAsync(output,token).ConfigureAwait(false);}
    if(!Valid(stage,a.FileBytes,a.FileHash))throw new InvalidDataException("OCR model integrity check failed");token.ThrowIfCancellationRequested();
    if(!Valid(target,a.FileBytes,a.FileHash)){try{File.Move(stage,target,true);}catch(IOException) when(Valid(target,a.FileBytes,a.FileHash)){} }
    finished+=a.PackageBytes;progress?.Report((finished,TotalBytes));
   }finally{if(File.Exists(stage))File.Delete(stage);if(File.Exists(archive))File.Delete(archive);}
  }}finally{Gate.Release();}
 }
}
