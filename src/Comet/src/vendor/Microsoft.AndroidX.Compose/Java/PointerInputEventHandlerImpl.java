package net.compose;

import androidx.compose.ui.input.pointer.PointerInputEventHandler;
import androidx.compose.ui.input.pointer.PointerInputScope;
import kotlin.Unit;
import kotlin.coroutines.Continuation;
import kotlin.jvm.functions.Function2;

/**
 * Java-side bridge that implements the empty
 * {@code androidx.compose.ui.input.pointer.PointerInputEventHandler}
 * interface (whose only abstract method is the suspend
 * {@code invoke(PointerInputScope)}, exposed in bytecode as the
 * {@code Object invoke(PointerInputScope, Continuation)} pair).
 *
 * <p>The .NET-for-Android binder strips
 * {@code PointerInputEventHandler.invoke} because its receiver is a
 * value-class-mangled type, so we can't implement it directly from C#
 * with the standard Android Callable Wrapper pipeline. Instead we
 * forward to a plain {@code Function2}, which IS bound and easy to
 * implement from C# (see {@code AndroidX.Compose.PointerInputBlock}).</p>
 */
final class PointerInputEventHandlerImpl implements PointerInputEventHandler {
    private final Function2<PointerInputScope, Continuation<? super Unit>, Object> block;

    public PointerInputEventHandlerImpl(
            Function2<PointerInputScope, Continuation<? super Unit>, Object> block) {
        this.block = block;
    }

    @Override
    public Object invoke(PointerInputScope scope, Continuation<? super Unit> continuation) {
        return block.invoke(scope, continuation);
    }
}
