# Subset of upstream 8be2f82fa16abb2bb1180b04a38e4de943b749e9 for this fork.
# Keep JNI entry points independently of ART profile inclusion.
-keep class mono.android.GCUserPeer {
    <init>();
    public void monodroidAddReference(java.lang.Object);
    public void monodroidClearReferences();
}

-keep class net.compose.PointerInputEventHandlerImpl {
    public <init>(kotlin.jvm.functions.Function2);
    public java.lang.Object invoke(androidx.compose.ui.input.pointer.PointerInputScope, kotlin.coroutines.Continuation);
}
