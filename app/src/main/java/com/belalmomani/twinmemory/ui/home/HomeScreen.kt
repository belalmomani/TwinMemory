package com.belalmomani.twinmemory.ui.home

import androidx.compose.foundation.BorderStroke
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.ColumnScope
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.systemBarsPadding
import androidx.compose.material3.Card
import androidx.compose.material3.CardDefaults
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import com.belalmomani.twinmemory.R
import com.belalmomani.twinmemory.ui.components.StatusChip
import com.belalmomani.twinmemory.ui.components.TmStatus
import com.belalmomani.twinmemory.ui.theme.TM

/**
 * Phase 2 shell. Shows only truthful state: no pairing/transfer layer exists yet, so the chip is
 * Offline and there are no actions that do nothing. Phase 3 adds storage and Save.
 */
@Composable
fun HomeScreen() {
    Column(
        Modifier.fillMaxSize().background(TM.Background).systemBarsPadding().padding(16.dp),
        verticalArrangement = Arrangement.spacedBy(14.dp),
    ) {
        Row(verticalAlignment = Alignment.CenterVertically) {
            Text(stringResource(R.string.app_name), style = MaterialTheme.typography.titleLarge, fontWeight = FontWeight.Bold, modifier = Modifier.weight(1f))
            StatusChip(TmStatus.Offline)
        }
        Card(
            colors = CardDefaults.cardColors(containerColor = TM.Surface),
            border = BorderStroke(1.dp, TM.Outline),
            modifier = Modifier.fillMaxWidth(),
        ) {
            Column(Modifier.padding(16.dp), verticalArrangement = Arrangement.spacedBy(6.dp)) {
                Text(stringResource(R.string.empty_title), fontWeight = FontWeight.SemiBold)
                Text(stringResource(R.string.empty_body), color = TM.Muted)
            }
        }
        Spacer(Modifier.weight(1f))
        Text(stringResource(R.string.tagline), color = TM.Muted, modifier = Modifier.align(Alignment.CenterHorizontally))
    }
}
