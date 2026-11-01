package com.belalmomani.twinmemory.ui

import android.text.format.Formatter
import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.layout.*
import androidx.compose.material3.*
import androidx.compose.runtime.Composable
import androidx.compose.runtime.getValue
import androidx.compose.ui.Modifier
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import com.belalmomani.twinmemory.BuildConfig
import com.belalmomani.twinmemory.R
import com.belalmomani.twinmemory.ui.theme.TM

@Composable
fun UpdateCard(vm: MainViewModel) {
    val st by vm.update.collectAsStateWithLifecycle()
    val ctx = LocalContext.current
    Card(colors = CardDefaults.cardColors(containerColor = TM.Surface), border = BorderStroke(1.dp, TM.Outline), modifier = Modifier.fillMaxWidth()) {
        Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(8.dp)) {
            Text(stringResource(R.string.upd_title), fontWeight = FontWeight.SemiBold)
            Text(stringResource(R.string.version, BuildConfig.VERSION_NAME, BuildConfig.VERSION_CODE), color = TM.Muted)
            when (val s = st) {
                UpdateUi.Idle -> {}
                UpdateUi.Checking -> Text(stringResource(R.string.upd_checking))
                UpdateUi.UpToDate -> Text(stringResource(R.string.upd_uptodate), color = TM.SuccessFg)
                is UpdateUi.Available -> {
                    Text(stringResource(R.string.upd_available, s.info.version), fontWeight = FontWeight.SemiBold)
                    Text(stringResource(R.string.upd_size, Formatter.formatShortFileSize(ctx, s.info.size)), color = TM.Muted)
                    if (s.info.notes.isNotBlank()) {
                        Text(stringResource(R.string.upd_notes_title), fontWeight = FontWeight.SemiBold)
                        Text(s.info.notes, color = TM.Muted, style = MaterialTheme.typography.bodySmall)
                    }
                    Button({ vm.downloadUpdate() }) { Text(stringResource(R.string.upd_now)) }
                }
                is UpdateUi.Downloading -> {
                    Text(stringResource(R.string.upd_downloading, s.pct))
                    LinearProgressIndicator(progress = { s.pct / 100f }, modifier = Modifier.fillMaxWidth())
                }
                is UpdateUi.Ready -> {
                    Text(stringResource(R.string.upd_ready))
                    Button({ vm.installUpdate() }) { Text(stringResource(R.string.upd_install)) }
                }
                is UpdateUi.Error -> {
                    Text(stringResource(s.msgRes), color = TM.ErrorFg)
                    if (s.detail.isNotBlank()) Text(s.detail, color = TM.Muted, style = MaterialTheme.typography.bodySmall)
                }
            }
            OutlinedButton({ vm.checkUpdates() }, enabled = st !is UpdateUi.Checking && st !is UpdateUi.Downloading) { Text(stringResource(R.string.upd_check)) }
        }
    }
}
