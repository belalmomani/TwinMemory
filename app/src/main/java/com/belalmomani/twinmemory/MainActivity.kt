package com.belalmomani.twinmemory

import android.content.Intent
import android.os.Bundle
import androidx.activity.ComponentActivity
import androidx.activity.compose.setContent
import androidx.activity.viewModels
import androidx.core.splashscreen.SplashScreen.Companion.installSplashScreen
import com.belalmomani.twinmemory.ui.MainScreen
import com.belalmomani.twinmemory.ui.MainViewModel
import com.belalmomani.twinmemory.ui.theme.TwinMemoryTheme

class MainActivity : ComponentActivity() {
    private val vm: MainViewModel by viewModels()

    override fun onCreate(savedInstanceState: Bundle?) {
        installSplashScreen()
        super.onCreate(savedInstanceState)
        if (savedInstanceState == null) vm.handleShare(intent) // share-sheet entry (not on rotation)
        setContent { TwinMemoryTheme { MainScreen(vm) } }
    }

    override fun onNewIntent(intent: Intent) {
        super.onNewIntent(intent)
        vm.handleShare(intent)
    }
}
