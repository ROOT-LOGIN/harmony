using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Harmony.ILCopying
{
	public static class Memory
	{
        public enum PROTECTION : uint
        {
            PAGE_NOACCESS = 0x01,
            PAGE_READONLY = 0x02,
            PAGE_READWRITE = 0x04,
            PAGE_WRITECOPY = 0x08,
            PAGE_EXECUTE = 0x10,
            PAGE_EXECUTE_READ = 0x20,
            PAGE_EXECUTE_READWRITE = 0x40,
            PAGE_EXECUTE_WRITECOPY = 0x80,
            PAGE_GUARD = 0x100,
            PAGE_NOCACHE = 0x200,
            PAGE_WRITECOMBINE = 0x400
        }

        [DllImport("kernel32")]
        public static extern bool VirtualProtect(IntPtr lpAddress, int dwSize, PROTECTION flNewProtect, out PROTECTION lpflOldProtect);

        public static long WriteJump(long memory, long destination, out byte[] orgBytes, out byte[] jmpBytes)
		{
            PROTECTION old;
            if (IntPtr.Size == sizeof(long))
			{
                var ptr = new IntPtr(memory);

                VirtualProtect(ptr, 16, PROTECTION.PAGE_EXECUTE_READWRITE, out old);

                orgBytes = new byte[16];
                ReadBytes(ptr.ToInt64(), orgBytes);

                memory = WriteBytes(memory, new byte[] { 0x48, 0xB8 });
				memory = WriteLong(memory, destination);
				memory = WriteBytes(memory, new byte[] { 0xFF, 0xE0 });

                jmpBytes = new byte[16];
                ReadBytes(ptr.ToInt64(), orgBytes);

                VirtualProtect(ptr, 16, old, out old);
            }
            else
			{
                var ptr = new IntPtr((int)memory);

                VirtualProtect(ptr, 6, PROTECTION.PAGE_EXECUTE_READWRITE, out old);

                orgBytes = new byte[6];
                ReadBytes(ptr.ToInt64(), orgBytes);

                memory = WriteByte(memory, 0x68);
				memory = WriteInt(memory, (int)destination);
				memory = WriteByte(memory, 0xc3);

                jmpBytes = new byte[6];
                ReadBytes(ptr.ToInt64(), jmpBytes);

                VirtualProtect(ptr, 6, old, out old);
            }
            return memory;
		}

		private static RuntimeMethodHandle GetRuntimeMethodHandle(MethodBase method)
		{
			if (method is DynamicMethod)
			{
				var nonPublicInstance = BindingFlags.NonPublic | BindingFlags.Instance;

				// DynamicMethod actually generates its m_methodHandle on-the-fly and therefore
				// we should call GetMethodDescriptor to force it to be created.
				//
				var m_GetMethodDescriptor = typeof(DynamicMethod).GetMethod("GetMethodDescriptor", nonPublicInstance);
				if (m_GetMethodDescriptor != null)
					return (RuntimeMethodHandle)m_GetMethodDescriptor.Invoke(method, new object[0]);

				// .Net Core
				var f_m_method = typeof(DynamicMethod).GetField("m_method", nonPublicInstance);
				if (f_m_method != null)
					return (RuntimeMethodHandle)f_m_method.GetValue(method);

				// Mono
				var f_mhandle = typeof(DynamicMethod).GetField("mhandle", nonPublicInstance);
				return (RuntimeMethodHandle)f_mhandle.GetValue(method);
			}

			return method.MethodHandle;
		}

		public static long GetMethodStart(MethodBase method)
		{
			// Required in .NET Core so that the method is JITed and the method start does not change
			//
			var handle = GetRuntimeMethodHandle(method);
			RuntimeHelpers.PrepareMethod(handle);

			return handle.GetFunctionPointer().ToInt64();
		}

		public static unsafe long WriteByte(long memory, byte value)
		{
			var p = (byte*)memory;
			*p = value;
			return memory + sizeof(byte);
		}

		public static unsafe long WriteBytes(long memory, byte[] values)
		{
			foreach (var value in values)
				memory = WriteByte(memory, value);
			return memory;
		}

		public static unsafe long WriteInt(long memory, int value)
		{
			var p = (int*)memory;
			*p = value;
			return memory + sizeof(int);
		}

		public static unsafe long WriteLong(long memory, long value)
		{
			var p = (long*)memory;
			*p = value;
			return memory + sizeof(long);
		}

        public static unsafe void ReadBytes(long memory, byte[] array)
        {
            if (array == null || array.Length == 0) return;

            var pm = (byte*)memory;
            fixed (byte* ptr = &array[0])
            {
                byte* pa = ptr;
                for(int i=0;i<array.Length;i++)
                {
                    *pa = *pm;
                    pa++;
                    pm++;
                }
            }
        }
	}
}