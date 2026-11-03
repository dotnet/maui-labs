package com.microsoft.maui.essentials.ai.experimental

import android.app.Activity
import android.os.Bundle
import android.widget.TextView

class StructuredOutputProbeActivity : Activity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        setContentView(TextView(this).apply {
            text = "Gemini Nano structured-output device tests"
        })
    }
}
