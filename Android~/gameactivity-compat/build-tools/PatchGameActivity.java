/* Copyright 2026 VRWebView contributors. SPDX-License-Identifier: MIT */
import java.nio.file.*;
import java.util.*;
import org.objectweb.asm.*;
import org.objectweb.asm.commons.AdviceAdapter;

/** Maintainer-only, hash-pinned bytecode edit. Never runs during a Unity build. */
public final class PatchGameActivity implements Opcodes {
    static final String IC = "com/google/androidgamesdk/gametextinput/InputConnection";
    static final String MON = "com/t34400/vrwebview/gameime/ImeStateMonitor";
    static final String DESC = "L" + MON + ";";
    static final String FIELD = "vrwebviewMonitor";
    static final Set<String> MUTATIONS = new HashSet<>(Arrays.asList("setSelection", "setComposingText",
        "setComposingRegion", "finishComposingText", "commitText", "deleteSurroundingText",
        "deleteSurroundingTextInCodePoints", "sendKeyEvent"));
    static void monitor(MethodVisitor mv) {
        mv.visitVarInsn(ALOAD, 0);
        mv.visitFieldInsn(GETFIELD, IC, FIELD, DESC);
    }
    static void call(MethodVisitor mv, String method, String desc) {
        monitor(mv); mv.visitMethodInsn(INVOKEVIRTUAL, MON, method, desc, false);
    }
    public static void main(String[] args) throws Exception {
        byte[] source = Files.readAllBytes(Paths.get(args[1]));
        ClassReader reader = new ClassReader(source);
        ClassWriter writer = new ClassWriter(reader, ClassWriter.COMPUTE_MAXS);
        Set<String> seen = new HashSet<>();
        boolean connection = args[0].equals("connection");
        reader.accept(new ClassVisitor(ASM9, writer) {
            @Override public void visit(int v, int a, String n, String s, String parent, String[] interfaces) {
                if (connection) {
                    interfaces = Arrays.copyOf(interfaces, interfaces.length + 1);
                    interfaces[interfaces.length - 1] = MON + "$Owner";
                }
                super.visit(v, a, n, s, parent, interfaces);
                if (connection) super.visitField(ACC_PRIVATE | ACC_FINAL, FIELD, DESC, null, null).visitEnd();
            }
            @Override public MethodVisitor visitMethod(int access, String name, String desc, String sig, String[] ex) {
                if (connection && name.equals("getExtractedText")) {
                    seen.add(name);
                    MethodVisitor mv = super.visitMethod(access, name, desc, sig, ex);
                    mv.visitCode(); monitor(mv); mv.visitVarInsn(ALOAD, 1); mv.visitVarInsn(ILOAD, 2);
                    mv.visitMethodInsn(INVOKEVIRTUAL, MON, "extract", desc, false);
                    mv.visitInsn(ARETURN); mv.visitMaxs(0, 0); mv.visitEnd();
                    return null;
                }
                MethodVisitor base = super.visitMethod(access, name, desc, sig, ex);
                return new AdviceAdapter(ASM9, base, access, name, desc) {
                    @Override protected void onMethodEnter() {
                        if (!connection) return;
                        if (name.equals("restartInput")) call(mv, "restart", "()V");
                        if (name.equals("closeConnection")) call(mv, "close", "()V");
                        if (name.equals("setState")) call(mv, "begin", "()V");
                        if (name.equals("setSoftKeyboardActive")) {
                            monitor(mv); mv.visitVarInsn(ILOAD, 1); mv.visitMethodInsn(INVOKEVIRTUAL, MON, "activate", "(Z)V", false);
                        }
                    }
                    @Override public void visitMethodInsn(int opcode, String owner, String method, String signature, boolean itf) {
                        if (connection && (name.equals("setState") || name.equals("onKey")) && owner.equals(IC) && method.equals("restartInput")) {
                            mv.visitInsn(name.equals("setState") ? ICONST_0 : ICONST_1);
                            super.visitMethodInsn(INVOKESTATIC, MON, "stateApplied", "(L" + IC + ";I)V", false);
                            seen.add(name.equals("setState") ? "stateNotifyGate" : "keyNotifyGate");
                            return;
                        }
                        if (connection && name.equals("setSoftKeyboardActive")) {
                            if (owner.equals("android/view/inputmethod/InputMethodManager") && method.equals("showSoftInput")) {
                                // Leave the original IMM/view/flags stack intact while
                                // preparing Android input, then queue exactly one show.
                                mv.visitVarInsn(ALOAD, 0);
                                mv.visitMethodInsn(INVOKEVIRTUAL, IC, "restartInput", "()V", false);
                                mv.visitVarInsn(ALOAD, 0);
                                super.visitMethodInsn(INVOKESTATIC, MON, "queueShow",
                                    "(Landroid/view/inputmethod/InputMethodManager;Landroid/view/View;IL" + IC + ";)Z", false);
                                seen.add("orderedShow");
                                return;
                            }
                            if (owner.equals(IC) && method.equals("restartInput")) {
                                super.visitMethodInsn(INVOKESTATIC, MON, "finishActivation", "(L" + IC + ";)V", false);
                                seen.add("hideRestart");
                                return;
                            }
                        }
                        super.visitMethodInsn(opcode, owner, method, signature, itf);
                    }
                    @Override protected void onMethodExit(int opcode) {
                        if (opcode == ATHROW) return;
                        if (!connection) {
                            if (name.equals("onCreateInputConnection") && desc.equals("(Landroid/view/inputmethod/EditorInfo;)L" + IC + ";")) {
                                seen.add(name); mv.visitInsn(DUP); mv.visitVarInsn(ALOAD, 1);
                                mv.visitMethodInsn(INVOKESTATIC, MON, "opened", "(L" + IC + ";Landroid/view/inputmethod/EditorInfo;)V", false);
                            }
                            return;
                        }
                        if (name.equals("<init>")) {
                            seen.add(name);
                            mv.visitVarInsn(ALOAD, 0); mv.visitTypeInsn(NEW, MON); mv.visitInsn(DUP);
                            mv.visitVarInsn(ALOAD, 0); mv.visitVarInsn(ALOAD, 2);
                            mv.visitMethodInsn(INVOKESPECIAL, MON, "<init>", "(L" + IC + ";Landroid/view/View;)V", false);
                            mv.visitFieldInsn(PUTFIELD, IC, FIELD, DESC);
                        }
                        if (MUTATIONS.contains(name)) { seen.add(name); call(mv, "changed", "()V"); }
                        if (name.equals("setState")) { seen.add(name); call(mv, "end", "()Z"); mv.visitInsn(POP); }
                        if (name.equals("endBatchEdit")) { seen.add(name); mv.visitInsn(POP); call(mv, "end", "()Z"); }
                    }
                };
            }
            @Override public void visitEnd() {
                if (connection) {
                    MethodVisitor mv = super.visitMethod(ACC_PUBLIC, "vrwebviewImeMonitor", "()" + DESC, null, null);
                    mv.visitCode(); monitor(mv); mv.visitInsn(ARETURN); mv.visitMaxs(0, 0); mv.visitEnd();
                    mv = super.visitMethod(ACC_PUBLIC, "beginBatchEdit", "()Z", null, null);
                    mv.visitCode(); call(mv, "begin", "()V"); mv.visitInsn(ICONST_1); mv.visitInsn(IRETURN); mv.visitMaxs(0, 0); mv.visitEnd();
                }
                super.visitEnd();
            }
        }, ClassReader.EXPAND_FRAMES);
        Set<String> required = new HashSet<>();
        if (connection) { required.addAll(MUTATIONS); required.addAll(Arrays.asList("<init>", "getExtractedText", "endBatchEdit", "setState", "orderedShow", "hideRestart", "stateNotifyGate", "keyNotifyGate")); }
        else required.add("onCreateInputConnection");
        if (!seen.equals(required)) throw new IllegalStateException("Unexpected pinned class structure: " + seen);
        Files.write(Paths.get(args[2]), writer.toByteArray());
    }
}
