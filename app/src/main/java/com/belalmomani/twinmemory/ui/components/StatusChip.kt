package com.belalmomani.twinmemory.ui.components

import androidx.annotation.StringRes
import androidx.compose.foundation.background
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.Text
import androidx.compose.runtime.Composable
import androidx.compose.ui.Modifier
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.res.stringResource
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import com.belalmomani.twinmemory.R
import com.belalmomani.twinmemory.ui.theme.TM

/** Connection/transfer states from the approved design. */
enum class TmStatus(val bg: Color, val fg: Color, @StringRes val label: Int) {
    Connected(TM.SuccessBg, TM.SuccessFg, R.string.status_connected),
    Transferring(TM.PrimarySoft, TM.PrimaryDark, R.string.status_transferring),
    Waiting(TM.WarnBg, TM.WarnFg, R.string.status_waiting),
    Failed(TM.ErrorBg, TM.ErrorFg, R.string.status_failed),
    Offline(TM.IdleBg, TM.Muted, R.string.status_offline),
    Unpaired(TM.IdleBg, TM.Muted, R.string.status_unpaired),
}

@Composable
fun StatusChip(status: TmStatus, modifier: Modifier = Modifier) = StatusChip(stringResource(status.label), status.bg, status.fg, modifier)

@Composable
fun StatusChip(text: String, bg: Color, fg: Color, modifier: Modifier = Modifier) {
    Text(
        text = text, color = fg, fontSize = 12.sp, fontWeight = FontWeight.SemiBold,
        modifier = modifier.background(bg, RoundedCornerShape(50)).padding(horizontal = 10.dp, vertical = 4.dp),
    )
}
