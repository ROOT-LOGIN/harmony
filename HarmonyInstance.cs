using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace Harmony
{
#if RECORD_PATCH_STATE
    public class Patches
    {
        public readonly ReadOnlyCollection<Patch> Prefixes;
        public readonly ReadOnlyCollection<Patch> Postfixes;
        public readonly ReadOnlyCollection<Patch> Transpilers;

        public ReadOnlyCollection<int> Owners
        {
            get
            {
                var result = new HashSet<int>();
                result.UnionWith(Prefixes.Select(p => p.ownerid));
                result.UnionWith(Postfixes.Select(p => p.ownerid));
                result.UnionWith(Postfixes.Select(p => p.ownerid));
                return result.ToList().AsReadOnly();
            }
        }

        public Patches(Patch[] prefixes, Patch[] postfixes, Patch[] transpilers)
        {
            if (prefixes == null) prefixes = new Patch[0];
            if (postfixes == null) postfixes = new Patch[0];
            if (transpilers == null) transpilers = new Patch[0];

            Prefixes = prefixes.ToList().AsReadOnly();
            Postfixes = postfixes.ToList().AsReadOnly();
            Transpilers = transpilers.ToList().AsReadOnly();
        }
    }
#endif

    public class HarmonyInstance
    {
        public unsafe static bool IsBigEndian()
        {
            var ary = new byte[4] { 1, 0, 0, 0 };
            int i = 0;
            fixed (byte* b = &ary[0])
            {
                int* p = (int*)b;
                i = *p;
            }
            Debug.Assert((i == 0x01000000) != BitConverter.IsLittleEndian);
            return i == 0x01000000;
        }

        public static int CURRENT_ID_VALUE;
        public readonly int id;
        public string Name { get; private set; }

        public static bool DEBUG = false;

        static HarmonyInstance()
        {
            System.Threading.Interlocked.Exchange(ref CURRENT_ID_VALUE, 0x0);
        }

        HarmonyInstance()
        {
            id = System.Threading.Interlocked.Increment(ref CURRENT_ID_VALUE);
            //this.id = id;
        }

        /// <summary>
        /// 准备标识.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <returns></returns>
        /// <exception cref="System.Exception">id cannot be null</exception>
        public static HarmonyInstance Create(string name)
        {
            return new HarmonyInstance() { Name = name };
        }

        //
#if ALLOW_ATTRIBUTES
        public void PatchAll(Assembly assembly)
        {
            assembly.GetTypes().Do(type =>
            {
                var parentMethodInfos = type.GetHarmonyMethods();
                if (parentMethodInfos != null && parentMethodInfos.Count() > 0)
                {
                    var info = HarmonyMethod.Merge(parentMethodInfos);
                    var processor = new PatchProcessor(this, type, info);
                    processor.Patch();
                }
            });
        }
#endif

        public PatchInfoData Patch(MethodBase original, HarmonyMethod prefix, HarmonyMethod postfix, HarmonyMethod transpiler, PatchFlags flags)
        {
            var processor = new PatchProcessor(this, original, prefix, postfix, transpiler);
            return processor.Patch(flags);
        }

        //

#if RECORD_PATCH_STATE
        public Patches IsPatched(MethodBase method)
        {
            return PatchProcessor.IsPatched(method);
        }

        public IEnumerable<MethodBase> GetPatchedMethods()
        {
            return HarmonySharedState.GetPatchedMethods();
        }

        public Dictionary<string, Version> VersionInfo(out Version currentVersion)
        {
            currentVersion = Assembly.GetExecutingAssembly().GetName().Version;
            var assemblies = new Dictionary<string, Assembly>();
            GetPatchedMethods().Do(method =>
            {
                var info = HarmonySharedState.GetPatchInfo(method);
                info.prefixes.Do(fix => assemblies[fix.owner] = fix.patch.DeclaringType.Assembly);
                info.postfixes.Do(fix => assemblies[fix.owner] = fix.patch.DeclaringType.Assembly);
                info.transpilers.Do(fix => assemblies[fix.owner] = fix.patch.DeclaringType.Assembly);
            });

            var result = new Dictionary<string, Version>();
            assemblies.Do(info =>
            {
                /*var assemblyName = info.Value.GetReferencedAssemblies().FirstOrDefault(a => a.FullName.StartsWith("0Harmony, Version"));
				if (assemblyName != null)
					result[info.Key] = assemblyName.Version;
                */
                //result[info.Key] = info.Version;
            });
            return result;
        }
#endif
    }

    /// <summary>
    /// 用于生成原始的方法体
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class HarmonyDelegate
    {
        internal protected HarmonyDelegate(Delegate @delegate)
        {
            Delegate = @delegate;
        }

        public void Invoke(params object[] argv)
        {
            this.Delegate.DynamicInvoke(argv);
        }

        internal protected readonly Delegate Delegate;

        public static HarmonyDelegate Clone(MethodInfo source, TranspilerImpl transpiler = null)
        {
            var types = new List<Type>(source.GetParameters().Select(p => p.ParameterType));
            if (source.IsStatic == false)
            {
                types.Insert(0, source.DeclaringType);
            }

            var dm = new System.Reflection.Emit.DynamicMethod("", source.ReturnType, types.ToArray());
            var copier = new Harmony.ILCopying.MethodCopier(source, dm);
            copier.AddTranspiler(transpiler);
            copier.Emit(null);

            types.Add(source.ReturnType);
            // Use Expression to create Delegate type
            var delegateType = System.Linq.Expressions.Expression.GetDelegateType(types.ToArray());
            var __delegate = dm.CreateDelegate(delegateType);

            return new HarmonyDelegate(__delegate);
        }
    }

    /// <summary>
    /// 用于生成原始的方法体
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public class HarmonyDelegate<T> : HarmonyDelegate
    {
        private HarmonyDelegate(Delegate @delegate) : base(@delegate)
        {
        }

        public T Call { get { return (T)(object)base.Delegate; } }

        public new static HarmonyDelegate<T> Clone(MethodInfo source, TranspilerImpl transpiler = null)
        {
#if DEBUG
            Debug.Assert(typeof(Delegate).IsAssignableFrom(typeof(T)));
#endif
            var types = new List<Type>(source.GetParameters().Select(p => p.ParameterType));
            if (source.IsStatic == false)
            {
                types.Insert(0, source.DeclaringType);
            }

            var dm = new System.Reflection.Emit.DynamicMethod("", source.ReturnType, types.ToArray());
            var copier = new Harmony.ILCopying.MethodCopier(source, dm);
            copier.AddTranspiler(transpiler);
            copier.Emit(null);
            var __delegate = dm.CreateDelegate(typeof(T));
            return new HarmonyDelegate<T>(__delegate);
        }
    }
}
