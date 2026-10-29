package com.belalmomani.twinmemory.ui.theme

import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Shapes
import androidx.compose.material3.lightColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.unit.dp

/** Approved Phase 1 palette. Mirrors design/design-tokens.json. */
object TM {
    val Primary = Color(0xFF2F6FED)
    val PrimaryDark = Color(0xFF1E4FBF)
    val PrimarySoft = Color(0xFFE6EEFF)
    val Background = Color(0xFFF4F7FB)
    val Surface = Color(0xFFFFFFFF)
    val Outline = Color(0xFFDDE5F0)
    val Text = Color(0xFF1B2638)
    val Muted = Color(0xFF55657D)
    val SuccessBg = Color(0xFFE3F4EC); val SuccessFg = Color(0xFF14694A)
    val WarnBg = Color(0xFFFFF1D6); val WarnFg = Color(0xFF8A5300)
    val ErrorBg = Color(0xFFFBE4E4); val ErrorFg = Color(0xFF9B2C2C)
    val IdleBg = Color(0xFFEEF3FA)
}

private val Scheme = lightColorScheme(
    primary = TM.Primary, onPrimary = Color.White,
    primaryContainer = TM.PrimarySoft, onPrimaryContainer = TM.PrimaryDark,
    background = TM.Background, onBackground = TM.Text,
    surface = TM.Surface, onSurface = TM.Text,
    onSurfaceVariant = TM.Muted, outline = TM.Outline,
    error = TM.ErrorFg, errorContainer = TM.ErrorBg,
)

// The approved design is light-only; no dark scheme is defined yet.
@Composable
fun TwinMemoryTheme(content: @Composable () -> Unit) {
    MaterialTheme(
        colorScheme = Scheme,
        shapes = Shapes(small = RoundedCornerShape(10.dp), medium = RoundedCornerShape(14.dp), large = RoundedCornerShape(16.dp)),
        content = content,
    )
}
