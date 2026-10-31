using System.Globalization;
using System.Windows.Markup;

namespace TwinMemory;

/// <summary>Arabic text is the key; English is used when the Windows UI language is not Arabic.</summary>
public static class S
{
    public static bool Ar { get; set; } = true; // set from Settings (Arabic by default)

    static readonly Dictionary<string, string> En = new()
    {
        ["ملف"] = "File", ["تحرير"] = "Edit", ["عرض"] = "View", ["أدوات"] = "Tools", ["مساعدة"] = "Help",
        ["مجلد جديد"] = "New folder", ["حفظ نص أو رابط"] = "Save text or link", ["استيراد ملف…"] = "Import file…", ["إغلاق"] = "Close",
        ["تحديد الكل"] = "Select all", ["نسخ"] = "Copy", ["لصق"] = "Paste", ["إعادة تسمية"] = "Rename", ["حذف"] = "Delete",
        ["المفضلة"] = "Favorites", ["نقل إلى مجلد…"] = "Move to folder…", ["أيقونات"] = "Icons", ["تفاصيل"] = "Details",
        ["ترتيب حسب الاسم"] = "Sort by name", ["ترتيب حسب التاريخ"] = "Sort by date", ["ترتيب حسب الحجم"] = "Sort by size", ["ترتيب حسب النوع"] = "Sort by type",
        ["تحديث"] = "Refresh", ["اتصال الهاتف (المرحلة 4)"] = "Phone connection (phase 4)", ["سلة المحذوفات"] = "Trash",
        ["دليل الاستخدام"] = "User guide", ["حول التطبيق"] = "About", ["+ جديد ▾"] = "+ New ▾", ["عرض ▾"] = "View ▾",
        ["فتح"] = "Open", ["خصائص"] = "Properties", ["استرجاع"] = "Restore", ["حذف نهائي"] = "Delete permanently",
        ["ابحث في TwinMemory…"] = "Search TwinMemory…", ["النصوص"] = "Texts", ["الروابط"] = "Links", ["الصور"] = "Photos", ["الملفات"] = "Files",
        ["هاتف C"] = "Phone C", ["كمبيوتر D"] = "Computer D", ["الهاتف: غير مرتبط"] = "Phone: not paired",
        ["عناصر"] = "items", ["تم تحديد"] = "selected", ["الاسم"] = "Name", ["النوع"] = "Type", ["المصدر"] = "Source", ["الحجم"] = "Size", ["التاريخ"] = "Date",
        ["الكمبيوتر"] = "Computer", ["الهاتف"] = "Phone", ["نص"] = "Text", ["رابط"] = "Link", ["صورة"] = "Image", ["فيديو"] = "Video", ["مستند"] = "Document",
        ["بدون مجلد"] = "No folder", ["اسم المجلد"] = "Folder name", ["النص أو الرابط"] = "Text or link", ["الاسم الجديد"] = "New name",
        ["حفظ"] = "Save", ["إلغاء"] = "Cancel", ["نقل إلى"] = "Move to", ["لا توجد عناصر بعد. اضغط + جديد، أو الصق (Ctrl+V)، أو اسحب ملفات إلى النافذة."] = "No items yet. Click + New, paste (Ctrl+V), or drag files into the window.",
        ["لا توجد عناصر هنا."] = "Nothing here.", ["تم الحفظ"] = "Saved", ["تم الحفظ — يشبه عنصرًا محفوظًا مسبقًا"] = "Saved — similar to an existing item",
        ["تعذّر الحفظ. عناصرك السابقة سليمة."] = "Could not save. Your existing items are safe.", ["نُقل إلى سلة المحذوفات"] = "Moved to trash",
        ["سيُحذف العنصر وملفه نهائيًا من هذا الجهاز ولا يمكن التراجع. متابعة؟"] = "The item and its file will be permanently deleted from this device. Continue?",
        ["هذا النوع من الملفات قد ينفّذ برنامجًا. هل تريد فتحه فعلًا؟"] = "This file type can run a program. Open it anyway?",
        ["لا يوجد ما يمكن لصقه (نص أو ملفات)."] = "Nothing to paste (text or files).", ["المجلدات غير مدعومة بعد؛ اسحب ملفات فقط."] = "Folders are not supported yet; drag files only.",
        ["لا يمكن فتح هذا العنصر."] = "This item cannot be opened.", ["الحالة"] = "Status",
        ["الرابط/النص"] = "Link/Text", ["المكان"] = "Location", ["تاريخ الحفظ"] = "Saved",
        ["• + جديد ← حفظ نص/رابط أو استيراد ملف.\n• Ctrl+V للصق ما نسخته (نص أو ملفات)، أو اسحب الملفات إلى النافذة.\n• زر الفأرة الأيمن على عنصر: نسخ، إعادة تسمية، نقل، مفضلة.\n• المحذوفات تبقى في السلة حتى تحذفها نهائيًا.\n• ربط الهاتف والنقل بين الجهازين: المرحلة 4."]
            = "• + New → save text/link or import a file.\n• Ctrl+V pastes what you copied (text or files), or drag files into the window.\n• Right-click an item: copy, rename, move, favorite.\n• Deleted items stay in Trash until you delete them permanently.\n• Phone pairing and transfer: phase 4.",
        ["بياناتك محفوظة على هذا الكمبيوتر فقط ولا تُرفع إلى أي خدمة سحابية."] = "Your data is stored on this computer only and is not uploaded to any cloud service.",
        ["ربط الهاتف…"] = "Pair phone…",
        ["قائمة النقل"] = "Transfer queue",
        ["اللغة"] = "Language",
        ["العربية"] = "Arabic",
        ["تلقائي (حسب النظام)"] = "Automatic (system)",
        ["ستُعاد تشغيل التطبيق لتطبيق اللغة. متابعة؟"] = "The app will restart to apply the language. Continue?",
        ["ربط الهاتف"] = "Pair phone",
        ["رمز جديد"] = "New code",
        ["إلغاء ربط المحدد"] = "Unpair selected",
        ["الأجهزة المرتبطة"] = "Paired devices",
        ["الرمز صالح دقيقتين ويُستخدم مرة واحدة.\nعلى الهاتف: المزيد ← ربط الكمبيوتر، ثم أدخل العنوان والرمز.\nيجب أن يكون الجهازان على نفس الشبكة. اسمح لـ TwinMemory في جدار حماية Windows عند ظهور النافذة (الشبكة الخاصة)."] = "The code is valid for 2 minutes and works once.\nOn the phone: More → Pair computer, then enter the address and code.\nBoth devices must be on the same network. Allow TwinMemory in Windows Firewall when prompted (private network).",
        ["العنوان"] = "Address",
        ["لا توجد أجهزة مرتبطة"] = "No paired devices",
        ["منتهي"] = "Expired",
        ["الهاتف: متصل"] = "Phone: connected",
        ["الهاتف: مرتبط — غير متصل"] = "Phone: paired — offline",
        ["قيد الاستلام"] = "Receiving",
        ["تم — تحقّق الكمبيوتر من سلامة الملف"] = "Done — integrity verified",
        ["فشل"] = "Failed",
        ["الجهاز"] = "Device",
        ["تعذّر تشغيل خادم الربط"] = "Could not start the pairing server",
        ["لا توجد عمليات نقل بعد"] = "No transfers yet",
        ["الخادم"] = "Server",
        ["إرسال إلى الهاتف"] = "Send to phone",
        ["اربط الهاتف أولًا (أدوات ← ربط الهاتف…)"] = "Pair a phone first (Tools → Pair phone…)",
        ["أُضيف إلى قائمة النقل — يصل الهاتف عند فتح TwinMemory عليه"] = "Added to the transfer queue — it arrives when TwinMemory is open on the phone",
        ["بانتظار الهاتف"] = "Waiting for phone",
        ["تم — تحقّق الهاتف من سلامة الملف"] = "Done — phone verified integrity",
        ["الاتجاه"] = "Direction",
        ["من الهاتف"] = "From phone",
        ["إلى الهاتف"] = "To phone",
        ["تعذّرت مزامنة المجلد"] = "Folder sync failed",
        ["تمت مزامنة المجلد"] = "Folder synced",
        ["تم إيقاف حذف عدد كبير من الملفات احتياطًا — راجع مجلد TwinMemory"] = "Stopped a large deletion as a precaution — check the TwinMemory folder",
        ["فتح مجلد TwinMemory في المستكشف"] = "Open TwinMemory folder in Explorer",
        ["مزامنة المجلد الآن"] = "Sync folder now",
        ["مجلد المزامنة"] = "Sync folder",
        ["المنفذ"] = "Port",
        ["اختبار الخادم"] = "Test server",
        ["السماح في جدار الحماية"] = "Allow in firewall",
        ["نتيجة الاختبار"] = "Test result",
        ["الخادم يعمل"] = "Server is running",
        ["الخادم لا يعمل"] = "Server is not running",
        ["سجل الخادم"] = "Server log",
        ["طُلبت إضافة قاعدة جدار الحماية — وافق على طلب المدير ثم أعد المحاولة من الهاتف"] = "Firewall rule requested — approve the administrator prompt, then retry from the phone",
        ["تشغيل مع بدء Windows"] = "Start with Windows",
        ["الإصدار"] = "Version", ["مجلد البيانات"] = "Data folder",
    };

    public static string T(string ar) => Ar ? ar : (En.TryGetValue(ar, out var v) ? v : ar);
}

[MarkupExtensionReturnType(typeof(string))]
public class TExtension : MarkupExtension
{
    public string Key { get; set; } = "";
    public TExtension() { }
    public TExtension(string key) { Key = key; }
    public override object ProvideValue(IServiceProvider serviceProvider) => S.T(Key);
}
