using System.Globalization;
namespace CaptureZoo;
internal static class UiLanguage
{
 internal static string? TestLanguage;
 static string? selected;
 static string SettingsPath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CaptureZoo","language.txt");
 internal static bool English=>EffectiveLanguage(TestLanguage??selected??Read(),CultureInfo.CurrentUICulture)=="en";
 internal static string EffectiveLanguage(string value,CultureInfo culture)=>value is "en" or "ja"?value:culture.TwoLetterISOLanguageName=="ja"?"ja":"en";
 internal static string Read(){try{return File.Exists(SettingsPath)?File.ReadAllText(SettingsPath).Trim():"auto";}catch(IOException){return "auto";}catch(UnauthorizedAccessException){return "auto";}}
 internal static string Selection=>selected??Read();
 internal static void Set(string value){if(value is not("auto" or "en" or "ja"))throw new ArgumentException("Unknown language");Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);File.WriteAllText(SettingsPath+".tmp",value);File.Move(SettingsPath+".tmp",SettingsPath,true);selected=value;}
 static readonly Dictionary<string,string> Strings=new(){
        ["起動に失敗しました。CaptureZoo.log を送ってください。"]="Could not start CaptureZoo. Please include CaptureZoo.log in your report.",
        ["今すぐキャプチャ ({0})"]="Capture now ({0})",
        ["ショートカットキー"]="Capture shortcut",
        ["Windows起動時に自動起動"]="Start with Windows",
        ["終了"]="Exit",
        ["キャプチャを中止して待機に戻る"]="Cancel capture",
        ["CaptureZoo ({0}でキャプチャ)"]="CaptureZoo ({0} to capture)",
        ["{0}を登録できません。アイコンの右クリックで別のキーを選んでください。"]="Could not register {0}. Right-click the tray icon to choose another key.",
        ["{0} は他のアプリで使用されています。別のキーを選んでください。"]="{0} is used by another application. Please choose another key.",
        ["ショートカット設定を保存できませんでした。"]="Could not save the shortcut setting.",
        ["キャプチャに失敗しました。CaptureZoo.log を送ってください。"]="Capture failed. Please include CaptureZoo.log in your report.",
        ["色 {0}"]="Color {0}",
        ["ブラシサイズ {0}"]="Brush size {0}",
        ["ブラシサイズ"]="Brush size",
        ["キャンセル"]="Cancel",
        [" — コピー失敗（描画またはダブルクリックで再試行）"]=" — Copy failed (draw or double-click to retry)",
        ["CaptureZoo - 範囲選択"]="CaptureZoo - Select region",
        ["ウィンドウ  {0} × {1}"]="Window  {0} × {1}",
        ["ペンの色"]="Pen color",
        ["彩度と明るさ"]="Saturation and brightness",
        ["色相"]="Hue",
        ["新しい色"]="New color",
        ["元の色"]="Original color",
        ["適用"]="Apply",
        ["自動（Windows）"]="Automatic (Windows)",
 };
 internal static string T(string text){if(Strings.TryGetValue(text,out var english))return English?english:text;var pair=Strings.FirstOrDefault(p=>p.Value==text);return !English&&pair.Key!=null?pair.Key:text;}
 internal static string F(string text,params object[] args)=>string.Format(CultureInfo.CurrentCulture,T(text),args);
 internal static void Apply(Control control){control.Text=T(control.Text??"");if(control.AccessibleName!=null)control.AccessibleName=T(control.AccessibleName);foreach(Control child in control.Controls)Apply(child);}
 internal static void Apply(ToolStripItem item){item.Text=T(item.Text??"");if(item is ToolStripDropDownItem drop)foreach(ToolStripItem child in drop.DropDownItems)Apply(child);}
}
