package com.belalmomani.twinmemory.update

import android.content.Context
import android.content.Intent
import android.content.pm.PackageInfo
import android.content.pm.PackageManager
import android.net.Uri
import android.os.Build
import android.provider.Settings
import androidx.core.content.FileProvider
import com.belalmomani.twinmemory.BuildConfig
import com.belalmomani.twinmemory.net.sha256Hex
import org.json.JSONObject
import java.io.File
import java.io.FileNotFoundException
import java.io.IOException
import java.net.URL
import java.security.MessageDigest
import javax.net.ssl.HttpsURLConnection

data class UpdateInfo(val version: String, val notes: String, val versionCode: Int, val url: String, val sha256: String, val size: Long)

/**
 * Update source: the repository's latest GitHub Release (asset update.json, produced by the release workflow).
 * Before anything is offered for install the file's size and SHA-256 must match the manifest, the APK must be this app's package with
 * the announced higher versionCode, and its signing certificate must equal the installed one. Android's installer enforces the
 * signature again, and the user always confirms. Nothing is installed silently and user data is untouched (same package = update).
 */
object UpdateChecker {
    const val MANIFEST_URL = "https://github.com/belalmomani/TwinMemory/releases/latest/download/update.json"
    private const val TRUSTED_PREFIX = "https://github.com/belalmomani/TwinMemory/releases/"

    sealed interface CheckResult {
        data object UpToDate : CheckResult
        data class Available(val info: UpdateInfo) : CheckResult
        data class Failed(val reason: String) : CheckResult
    }

    private fun open(url: String, readMs: Int): HttpsURLConnection {
        val c = URL(url).openConnection() as HttpsURLConnection
        c.connectTimeout = 8000; c.readTimeout = readMs
        c.setRequestProperty("User-Agent", "TwinMemory/" + BuildConfig.VERSION_NAME)
        return c
    }

    fun check(currentCode: Int): CheckResult = try {
        val text = open(MANIFEST_URL, 15000).inputStream.bufferedReader().use { it.readText() }
        val o = JSONObject(text)
        val a = o.getJSONObject("android")
        val url = a.getString("url")
        if (!url.startsWith(TRUSTED_PREFIX)) CheckResult.Failed("untrusted source")
        else {
            val info = UpdateInfo(o.optString("version"), o.optString("notes"), a.getInt("versionCode"), url, a.getString("sha256").lowercase(), a.getLong("size"))
            if (info.versionCode > currentCode) CheckResult.Available(info) else CheckResult.UpToDate
        }
    } catch (e: FileNotFoundException) { CheckResult.Failed("no release published yet")
    } catch (e: IOException) { CheckResult.Failed(e.message ?: "network")
    } catch (e: Exception) { CheckResult.Failed("bad manifest") }

    @Throws(IOException::class)
    fun download(info: UpdateInfo, dest: File, onProgress: (Int) -> Unit) {
        dest.parentFile?.mkdirs()
        val part = File(dest.path + ".part")
        val c = open(info.url, 30000)
        c.inputStream.use { input ->
            part.outputStream().use { out ->
                val buf = ByteArray(64 * 1024); var done = 0L; var last = -1
                while (true) {
                    val n = input.read(buf); if (n < 0) break
                    out.write(buf, 0, n); done += n
                    val p = if (info.size > 0) (done * 100 / info.size).toInt().coerceIn(0, 100) else 0
                    if (p != last) { last = p; onProgress(p) }
                }
            }
        }
        if (dest.exists()) dest.delete()
        if (!part.renameTo(dest)) throw IOException("rename failed")
    }

    private fun sha256(f: File): String {
        val md = MessageDigest.getInstance("SHA-256")
        f.inputStream().use { i -> val b = ByteArray(64 * 1024); while (true) { val n = i.read(b); if (n < 0) break; md.update(b, 0, n) } }
        return md.digest().joinToString("") { "%02x".format(it) }
    }

    @Suppress("DEPRECATION")
    private fun signers(p: PackageInfo): Set<String> {
        val sigs = if (Build.VERSION.SDK_INT >= 28) p.signingInfo?.apkContentsSigners else p.signatures
        return sigs?.map { sha256Hex(it.toByteArray()) }?.toSet() ?: emptySet()
    }

    /** Returns null when the package is safe to offer for install, otherwise a short code: size, hash, bad_apk, package, version, signature. */
    @Suppress("DEPRECATION")
    fun verify(ctx: Context, file: File, info: UpdateInfo): String? {
        if (file.length() != info.size) return "size"
        if (sha256(file) != info.sha256) return "hash"
        val pm = ctx.packageManager
        val flags = if (Build.VERSION.SDK_INT >= 28) PackageManager.GET_SIGNING_CERTIFICATES else PackageManager.GET_SIGNATURES
        val arch = pm.getPackageArchiveInfo(file.path, flags) ?: return "bad_apk"
        if (arch.packageName != ctx.packageName) return "package"
        val code = if (Build.VERSION.SDK_INT >= 28) arch.longVersionCode else arch.versionCode.toLong()
        if (code != info.versionCode.toLong() || code <= BuildConfig.VERSION_CODE) return "version"
        val newSig = signers(arch)
        val cur = signers(pm.getPackageInfo(ctx.packageName, flags))
        // Refuse only when both are readable and different; the system installer enforces the signature again.
        if (newSig.isNotEmpty() && cur.isNotEmpty() && newSig.intersect(cur).isEmpty()) return "signature"
        return null
    }

    fun canInstall(ctx: Context): Boolean = Build.VERSION.SDK_INT < 26 || ctx.packageManager.canRequestPackageInstalls()

    fun openInstallSettings(ctx: Context) {
        ctx.startActivity(Intent(Settings.ACTION_MANAGE_UNKNOWN_APP_SOURCES, Uri.parse("package:" + ctx.packageName)).addFlags(Intent.FLAG_ACTIVITY_NEW_TASK))
    }

    fun install(ctx: Context, file: File) {
        val uri = FileProvider.getUriForFile(ctx, ctx.packageName + ".files", file)
        ctx.startActivity(
            Intent(Intent.ACTION_VIEW).setDataAndType(uri, "application/vnd.android.package-archive")
                .addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION or Intent.FLAG_ACTIVITY_NEW_TASK)
        )
    }
}
