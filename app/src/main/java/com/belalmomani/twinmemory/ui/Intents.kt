package com.belalmomani.twinmemory.ui

import android.content.ActivityNotFoundException
import android.content.ClipData
import android.content.ClipboardManager
import android.content.Context
import android.content.Intent
import android.net.Uri
import androidx.core.content.FileProvider
import com.belalmomani.twinmemory.data.db.ItemEntity
import java.io.File

private fun uriFor(ctx: Context, f: File): Uri = FileProvider.getUriForFile(ctx, ctx.packageName + ".files", f)

fun copyText(ctx: Context, item: ItemEntity) {
    (ctx.getSystemService(Context.CLIPBOARD_SERVICE) as ClipboardManager).setPrimaryClip(ClipData.newPlainText(item.title, item.text ?: ""))
}

fun shareItem(ctx: Context, item: ItemEntity, file: File?) {
    val i = Intent(Intent.ACTION_SEND)
    if (file != null) {
        val u = uriFor(ctx, file)
        i.type = item.mimeType ?: "*/*"; i.putExtra(Intent.EXTRA_STREAM, u)
        i.clipData = ClipData.newRawUri("", u); i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION)
    } else { i.type = "text/plain"; i.putExtra(Intent.EXTRA_TEXT, item.text ?: "") }
    ctx.startActivity(Intent.createChooser(i, null))
}

/** Returns false when no app can open it (caller shows a message). */
fun openItem(ctx: Context, item: ItemEntity, file: File?): Boolean {
    val i = Intent(Intent.ACTION_VIEW)
    if (file != null) { i.setDataAndType(uriFor(ctx, file), item.mimeType ?: "*/*"); i.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION) }
    else if (item.kind == "LINK") i.data = Uri.parse(item.text?.trim())
    else return false
    return try { ctx.startActivity(i); true } catch (e: ActivityNotFoundException) { false }
}
