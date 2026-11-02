package com.belalmomani.twinmemory.transfer

import android.net.Uri
import androidx.documentfile.provider.DocumentFile
import com.belalmomani.twinmemory.TwinMemoryApp
import com.belalmomani.twinmemory.data.SaveResult
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.withContext

/** Imports a directory tree picked with the system folder picker. The originals are copied, never changed or deleted. */
class TreeImporter(private val app: TwinMemoryApp) {
    suspend fun run(tree: Uri, baseFolderId: String?): Int = withContext(Dispatchers.IO) {
        val root = DocumentFile.fromTreeUri(app, tree) ?: return@withContext 0
        val start = listOfNotNull(app.repo.folderPath(baseFolderId), root.name).joinToString("/")
        app.repo.ensureFolderPath(start)
        var n = 0
        suspend fun walk(dir: DocumentFile, path: String, depth: Int) {
            if (depth > 20) return
            for (f in dir.listFiles()) {
                val name = f.name ?: continue
                if (name.startsWith(".")) continue
                if (f.isDirectory) {
                    val p = "$path/$name"
                    app.repo.ensureFolderPath(p)
                    walk(f, p, depth + 1)
                } else if (f.isFile) {
                    val fid = app.repo.ensureFolderPath(path)
                    val (r, _) = app.repo.importUriId(f.uri, fid, "phone")
                    if (r != SaveResult.FAILED) n++
                }
            }
        }
        walk(root, start, 0)
        n
    }
}
