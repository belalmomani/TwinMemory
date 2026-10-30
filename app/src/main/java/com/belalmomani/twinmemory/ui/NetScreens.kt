package com.belalmomani.twinmemory.ui

import android.text.format.Formatter
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.*
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.text.KeyboardOptions
import androidx.compose.foundation.verticalScroll
import androidx.compose.material.icons.Icons
import androidx.compose.material.icons.automirrored.filled.ArrowBack
import androidx.compose.material3.*
import androidx.compose.runtime.*
import androidx.compose.runtime.saveable.rememberSaveable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.text.input.KeyboardCapitalization
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.belalmomani.twinmemory.R
import com.belalmomani.twinmemory.data.db.TransferEntity
import com.belalmomani.twinmemory.ui.components.StatusChip
import com.belalmomani.twinmemory.ui.theme.TM

/** Transfer queue from the database. "Done" is shown only after the computer verified the file (see SendWorker). */
@Composable
fun QueueScreen(list: List<TransferEntity>, onBack: () -> Unit, onRetry: (String) -> Unit) {
    val ctx = LocalContext.current
    Column(Modifier.fillMaxSize()) {
        Row(Modifier.padding(8.dp), verticalAlignment = Alignment.CenterVertically) {
            IconButton(onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, stringResource(R.string.back)) }
            Text(stringResource(R.string.tool_queue), fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleMedium)
        }
        if (list.isEmpty()) {
            Box(Modifier.fillMaxSize().padding(32.dp), Alignment.Center) { Text(stringResource(R.string.queue_empty), color = TM.Muted) }
            return
        }
        LazyColumn(contentPadding = PaddingValues(16.dp, 0.dp, 16.dp, 24.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            items(list, key = { it.id }) { t ->
                Card(colors = CardDefaults.cardColors(containerColor = TM.Surface), border = BorderStroke(1.dp, TM.Outline), modifier = Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(14.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                        Row(verticalAlignment = Alignment.CenterVertically) {
                            Text(t.title, fontWeight = FontWeight.SemiBold, maxLines = 1, modifier = Modifier.weight(1f))
                            val pct = if (t.size > 0) (t.sent * 100 / t.size).toInt() else 0
                            when (t.state) {
                                "DONE" -> StatusChip(stringResource(R.string.st_done), TM.SuccessBg, TM.SuccessFg)
                                "FAILED" -> StatusChip(stringResource(R.string.st_failed), TM.ErrorBg, TM.ErrorFg)
                                "SENDING" -> StatusChip(stringResource(R.string.st_sending, pct), TM.PrimarySoft, TM.PrimaryDark)
                                "WAITING" -> StatusChip(stringResource(R.string.st_waiting), TM.WarnBg, TM.WarnFg)
                                else -> StatusChip(stringResource(R.string.st_pending), TM.WarnBg, TM.WarnFg)
                            }
                        }
                        if (t.size > 0) Text(Formatter.formatShortFileSize(ctx, t.size), color = TM.Muted, style = MaterialTheme.typography.bodySmall)
                        if (t.state == "SENDING" && t.size > 0) LinearProgressIndicator(progress = { t.sent.toFloat() / t.size }, modifier = Modifier.fillMaxWidth())
                        if (t.state == "FAILED") {
                            Text(t.error ?: "", color = TM.ErrorFg, style = MaterialTheme.typography.bodySmall)
                            TextButton({ onRetry(t.id) }) { Text(stringResource(R.string.retry)) }
                        }
                    }
                }
            }
        }
    }
}

@Composable
fun PairScreen(vm: MainViewModel, onBack: () -> Unit) {
    val conn by vm.conn.collectAsStateWithLifecycle()
    val pc by vm.pcName.collectAsStateWithLifecycle()
    val found by vm.found.collectAsStateWithLifecycle()
    val busy by vm.busy.collectAsStateWithLifecycle()
    var host by rememberSaveable { mutableStateOf("") }
    var port by rememberSaveable { mutableStateOf("47821") }
    var code by rememberSaveable { mutableStateOf("") }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(8.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            IconButton(onBack) { Icon(Icons.AutoMirrored.Filled.ArrowBack, stringResource(R.string.back)) }
            Text(stringResource(R.string.pair_title), fontWeight = FontWeight.Bold, style = MaterialTheme.typography.titleMedium)
        }
        Column(Modifier.padding(8.dp, 0.dp), verticalArrangement = Arrangement.spacedBy(12.dp)) {
            if (conn != Conn.UNPAIRED) {
                Card(colors = CardDefaults.cardColors(containerColor = TM.Surface), border = BorderStroke(1.dp, TM.Outline), modifier = Modifier.fillMaxWidth()) {
                    Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
                        Text(stringResource(R.string.paired_with, pc ?: ""), fontWeight = FontWeight.SemiBold)
                        if (conn == Conn.ONLINE) StatusChip(stringResource(R.string.status_connected), TM.SuccessBg, TM.SuccessFg)
                        else StatusChip(stringResource(R.string.status_offline), TM.IdleBg, TM.Muted)
                        OutlinedButton({ vm.unpair() }) { Text(stringResource(R.string.unpair)) }
                    }
                }
            } else {
                Text(stringResource(R.string.pair_help), color = TM.Muted)
                OutlinedButton({ vm.discover() }, Modifier.fillMaxWidth()) { Text(stringResource(R.string.discover)) }
                found.forEach { f ->
                    TextButton({ host = f.host; port = f.port.toString() }, Modifier.fillMaxWidth()) { Text("${f.name} — ${f.host}") }
                }
                OutlinedTextField(host, { host = it.trim() }, singleLine = true, label = { Text(stringResource(R.string.pair_host)) }, modifier = Modifier.fillMaxWidth())
                OutlinedTextField(port, { port = it.filter(Char::isDigit) }, singleLine = true, label = { Text(stringResource(R.string.pair_port)) }, modifier = Modifier.fillMaxWidth())
                OutlinedTextField(
                    code, { code = it.uppercase() }, singleLine = true, label = { Text(stringResource(R.string.pair_code)) },
                    keyboardOptions = KeyboardOptions(capitalization = KeyboardCapitalization.Characters), modifier = Modifier.fillMaxWidth(),
                )
                Button(
                    { vm.pair(host, port.toIntOrNull() ?: 47821, code) },
                    enabled = !busy && host.isNotBlank() && code.filter { it.isLetterOrDigit() }.length >= 8,
                    modifier = Modifier.fillMaxWidth(),
                ) { Text(stringResource(R.string.pair_btn)) }
            }
        }
    }
}
