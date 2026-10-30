package com.belalmomani.twinmemory.net

import android.util.Base64
import org.json.JSONArray
import org.json.JSONObject
import java.io.File
import java.io.FileOutputStream
import java.io.IOException
import java.net.URL
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec
import javax.net.ssl.HostnameVerifier
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.X509TrustManager

fun sha256Hex(b: ByteArray): String = MessageDigest.getInstance("SHA-256").digest(b).joinToString("") { "%02x".format(it) }

/**
 * Talks to the TwinMemory server on the user's computer over TLS. After pairing the server certificate is pinned (SHA-256
 * fingerprint); a different certificate is rejected. During pairing (pin == null) the fingerprint is not trusted by itself:
 * it is bound into an HMAC proof keyed by the one-time code, which only the real server can verify.
 */
class PcClient(private val host: String, private val port: Int, private val pin: String?, private val token: String?, private val deviceId: String) {
    class HttpError(val code: Int, val body: String) : IOException("HTTP $code $body")
    data class PairResult(val token: String, val fingerprint: String, val pcName: String)

    private val trust = object : X509TrustManager {
        override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) {}
        override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
            if (pin != null && sha256Hex(chain[0].encoded) != pin) throw CertificateException("certificate pin mismatch")
        }
        override fun getAcceptedIssuers(): Array<X509Certificate> = arrayOf()
    }
    private val ssl = SSLContext.getInstance("TLS").apply { init(null, arrayOf(trust), null) }.socketFactory

    private fun open(path: String, method: String, readMs: Int = 15000): HttpsURLConnection {
        val c = URL("https://$host:$port$path").openConnection() as HttpsURLConnection
        c.sslSocketFactory = ssl
        c.hostnameVerifier = HostnameVerifier { _, _ -> true } // identity is proven by the pinned fingerprint, not the host name
        c.connectTimeout = 4000; c.readTimeout = readMs; c.requestMethod = method
        c.setRequestProperty("X-Device-Id", deviceId)
        if (token != null) c.setRequestProperty("Authorization", "Bearer $token")
        return c
    }

    private fun finish(c: HttpsURLConnection): JSONObject {
        val code = c.responseCode
        val text = (if (code in 200..299) c.inputStream else c.errorStream)?.bufferedReader()?.use { it.readText() } ?: ""
        if (code !in 200..299) throw HttpError(code, text)
        return if (text.isBlank()) JSONObject() else JSONObject(text)
    }

    private fun postJson(path: String, body: JSONObject, readMs: Int = 15000): JSONObject {
        val bytes = body.toString().toByteArray()
        val c = open(path, "POST", readMs)
        c.doOutput = true; c.setFixedLengthStreamingMode(bytes.size); c.setRequestProperty("Content-Type", "application/json")
        c.outputStream.use { it.write(bytes) }
        return finish(c)
    }

    fun ping(): Boolean = finish(open("/ping", "GET")).optBoolean("ok")

    fun pair(code: String, deviceName: String): PairResult {
        val c = open("/pair", "POST")
        c.doOutput = true; c.setChunkedStreamingMode(0); c.setRequestProperty("Content-Type", "application/json")
        c.connect()
        val fp = sha256Hex(c.serverCertificates[0].encoded)
        val clean = code.uppercase().filter { it.isLetterOrDigit() }
        val mac = Mac.getInstance("HmacSHA256").apply { init(SecretKeySpec(clean.toByteArray(), "HmacSHA256")) }
        val proof = Base64.encodeToString(mac.doFinal("TM-PAIR|$deviceId|$fp".toByteArray()), Base64.NO_WRAP)
        val body = JSONObject().put("deviceId", deviceId).put("deviceName", deviceName).put("proof", proof).toString().toByteArray()
        c.outputStream.use { it.write(body) }
        val r = finish(c)
        return PairResult(r.getString("token"), fp, r.optString("name"))
    }

    fun unpair() { finish(open("/pair", "DELETE")) }

    fun startTransfer(meta: JSONObject): JSONObject = postJson("/transfers", meta)

    fun putChunk(id: String, offset: Long, buf: ByteArray, len: Int): JSONObject {
        val c = open("/transfers/$id/data?offset=$offset", "PUT", 60000)
        c.doOutput = true; c.setFixedLengthStreamingMode(len); c.setRequestProperty("Content-Type", "application/octet-stream")
        c.outputStream.use { it.write(buf, 0, len) }
        return finish(c)
    }

    /** The computer verifies size and SHA-256 here; success means the file arrived intact. */
    fun complete(id: String): JSONObject {
        val c = open("/transfers/$id/complete", "POST", 180000)
        c.doOutput = true; c.setFixedLengthStreamingMode(0)
        c.outputStream.close()
        return finish(c)
    }

    // ---- computer -> phone (the phone pulls) ----
    fun listOutbox(): JSONArray = finish(open("/outbox", "GET")).optJSONArray("items") ?: JSONArray()

    /** Resumes from the existing part file using an HTTP Range request. */
    fun download(id: String, part: File) {
        val existing = if (part.exists()) part.length() else 0L
        val c = open("/outbox/$id/data", "GET", 60000)
        if (existing > 0) c.setRequestProperty("Range", "bytes=$existing-")
        val code = c.responseCode
        if (code == 416) return // nothing left to download; the caller verifies size and hash
        if (code !in 200..299) throw HttpError(code, c.errorStream?.bufferedReader()?.use { it.readText() } ?: "")
        part.parentFile?.mkdirs()
        c.inputStream.use { input -> FileOutputStream(part, code == 206).use { out -> input.copyTo(out, 64 * 1024) } }
    }

    fun ack(id: String) { finish(open("/outbox/$id/ack", "POST").also { it.doOutput = true; it.setFixedLengthStreamingMode(0); it.outputStream.close() }) }
    fun fail(id: String) { finish(open("/outbox/$id/fail", "POST").also { it.doOutput = true; it.setFixedLengthStreamingMode(0); it.outputStream.close() }) }
}
