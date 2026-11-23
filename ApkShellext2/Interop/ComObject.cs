using System;
using System.Runtime.InteropServices;

namespace ApkShellext2.Interop {
    /// <summary>
    /// Raw vtable access for COM pointers that cross the shell boundary.
    ///
    /// The handler classes are created by <c>mscoree.dll</c>, so the CLR owns the server side and
    /// the C# class only has to declare the interface it implements. The *client* side - the shell
    /// passing us <c>IShellFolder</c>, <c>IDataObject</c> or an <c>IStream</c> - is the fragile
    /// direction: casting those pointers to a <c>[ComImport]</c> interface builds an RCW, and an
    /// RCW wants to be released with <c>Marshal.ReleaseComObject</c>, which drops a reference the
    /// shell still owns - the premature release behind the <c>ShellExtInitServer.cs:52</c> crash.
    ///
    /// This class reads the function pointer out of the vtable by slot number and calls it through
    /// a delegate instead. No RCW, no reference counted identity, no hidden AddRef, and the slot
    /// number is checked against the declaration by <see cref="ComLayout"/> (InteropLayoutTests
    /// pins the slot numbers themselves).
    /// </summary>
    internal static class ComObject {
        //  IUnknown occupies slots 0..2 of every COM vtable, the first interface method is slot 3.
        internal const int SlotQueryInterface = 0;
        internal const int SlotAddRef = 1;
        internal const int SlotRelease = 2;
        internal const int FirstMethodSlot = 3;

        /// <summary>
        /// Calls <c>IUnknown::QueryInterface</c>. On success the caller owns the returned reference
        /// and must release it (<see cref="SafeRelease"/>).
        /// </summary>
        internal static int QueryInterface(IntPtr instance, Guid iid, out IntPtr ppv) {
            if (instance == IntPtr.Zero) {
                ppv = IntPtr.Zero;
                return HResult.E_POINTER;
            }
            return Marshal.QueryInterface(instance, ref iid, out ppv);
        }

        /// <summary>
        /// Asks the object whether it implements <paramref name="iid"/> and releases the reference
        /// the probe took. Useful to decide between the IContextMenu, 2 and 3 contract.
        /// </summary>
        internal static bool Supports(IntPtr instance, Guid iid) {
            IntPtr probe;
            if (HResult.Failed(QueryInterface(instance, iid, out probe))) {
                return false;
            }
            SafeRelease(ref probe);
            return true;
        }

        /// <summary>
        /// <c>IUnknown::AddRef</c>: the CLR wrapper the shell understands; only needed when a
        /// pointer is stored beyond the call that produced it.
        /// </summary>
        internal static uint AddRef(IntPtr instance) {
            if (instance == IntPtr.Zero) {
                return 0;
            }
            return (uint)Marshal.AddRef(instance);
        }

        /// <summary>
        /// <c>IUnknown::Release</c>. Returns the remaining reference count, as the COM method does;
        /// the pointer is invalid afterwards whether or not it reaches zero.
        /// </summary>
        internal static int Release(IntPtr instance) {
            if (instance == IntPtr.Zero) {
                return 0;
            }
            return Marshal.Release(instance);
        }

        /// <summary>Releases <paramref name="instance"/> and clears it, so a double release cannot happen.</summary>
        internal static void SafeRelease(ref IntPtr instance) {
            IntPtr value = instance;
            instance = IntPtr.Zero;
            if (value != IntPtr.Zero) {
                Marshal.Release(value);
            }
        }

        /// <summary>
        /// Returns the function pointer at <paramref name="slot"/>. <paramref name="slot"/> 3 is the
        /// first method of the declared interface; 0..2 are the IUnknown slots.
        /// </summary>
        internal static IntPtr GetSlotPointer(IntPtr instance, int slot) {
            if (instance == IntPtr.Zero) {
                throw new ArgumentNullException(nameof(instance));
            }
            if (slot < 0) {
                throw new ArgumentOutOfRangeException(nameof(slot), slot, "A vtable slot cannot be negative.");
            }
            IntPtr vtable = Marshal.ReadIntPtr(instance);
            if (vtable == IntPtr.Zero) {
                throw new InvalidOperationException("The COM object has a null vtable pointer.");
            }
            IntPtr method = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            if (method == IntPtr.Zero) {
                throw new NotSupportedException("Vtable slot " + slot + " is empty; the interface version is older than the declaration.");
            }
            return method;
        }

        /// <summary>
        /// Binds a vtable slot to a delegate type. <typeparamref name="T"/> must be declared with
        /// <c>[UnmanagedFunctionPointer(CallingConvention.Winapi)]</c> (equivalent to StdCall on
        /// x86, no-op on x64) and repeat the interface method's parameters exactly.
        /// </summary>
        internal static T GetDelegate<T>(IntPtr instance, int slot) where T : Delegate {
            IntPtr method = GetSlotPointer(instance, slot);
            return (T)Marshal.GetDelegateForFunctionPointer(method, typeof(T));
        }
    }
}
