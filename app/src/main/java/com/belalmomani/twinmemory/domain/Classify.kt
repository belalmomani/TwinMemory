package com.belalmomani.twinmemory.domain

enum class ItemKind { TEXT, LINK, IMAGE, VIDEO, DOCUMENT, FILE }
enum class SortBy { DATE, NAME, SIZE, TYPE }

/** Pure logic (no Android dependencies) so it can be unit-tested on the JVM. */
object Classify {
    private val docExt = setOf("pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "txt", "md", "csv", "rtf", "odt", "ods", "odp")

    fun kindFromMime(mime: String?, fileName: String?): ItemKind {
        val m = mime?.lowercase().orEmpty()
        val ext = fileName?.substringAfterLast('.', "")?.lowercase().orEmpty()
        return when {
            m.startsWith("image/") -> ItemKind.IMAGE
            m.startsWith("video/") -> ItemKind.VIDEO
            m == "application/pdf" || m.startsWith("text/") || m.contains("officedocument") || m == "application/msword" || ext in docExt -> ItemKind.DOCUMENT
            else -> ItemKind.FILE
        }
    }

    fun isSingleUrl(s: String): Boolean {
        val t = s.trim()
        return t.length > 8 && (t.startsWith("http://") || t.startsWith("https://")) && t.none { it.isWhitespace() }
    }

    fun hostOf(url: String): String = url.trim().substringAfter("://").substringBefore('/').substringBefore('?').substringBefore('#')

    fun titleFor(text: String): String =
        text.lineSequence().map { it.trim() }.firstOrNull { it.isNotEmpty() }?.take(60) ?: ""

    /** Removes path separators/control characters so a name can never escape the storage folder. */
    fun sanitizeFileName(name: String?): String {
        val cleaned = (name ?: "").replace(Regex("[\\\\/\\u0000-\\u001F:*?\"<>|]"), "_").trim().trimStart('.')
        return cleaned.take(120).ifEmpty { "file" }
    }

    /** Escapes LIKE wildcards (escape char is backslash) and wraps in %…%; null when the query is blank. */
    fun likePattern(q: String): String? {
        val t = q.trim()
        if (t.isEmpty()) return null
        return "%" + t.replace("\\", "\\\\").replace("%", "\\%").replace("_", "\\_") + "%"
    }
}
