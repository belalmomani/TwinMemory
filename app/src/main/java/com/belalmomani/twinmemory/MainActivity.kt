package com.belalmomani.twinmemory

import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import com.belalmomani.twinmemory.ui.home.HomeScreen
import com.belalmomani.twinmemory.ui.theme.TwinMemoryTheme

class MainActivity : ComponentActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        installSplashScreen()
        super.onCreate(savedInstanceState)
        setContent { TwinMemoryTheme { HomeScreen() } }
    }
}
