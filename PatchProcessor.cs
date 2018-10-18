using System;
using System.Collections.Generic;
using System.Reflection;

namespace Harmony
{
    public enum PatchFlags
    {
        /// <summary>
        /// generate use prefix, target body and postfix
        /// </summary>
        PF_Default = 0,
        /// <summary>
        /// generate use prefix and postfix
        /// </summary>
        PF_NoOrigin = 1,
        /// <summary>
        /// generate just use postfix
        /// </summary>
        PF_Detour = 2,
    }

    /// <summary>
    /// Patch处理器. 
    /// </summary>
    public class PatchProcessor
	{
		static object locker = new object();

		readonly HarmonyInstance instance;

#if ALLOW_ATTRIBUTES
		readonly Type container;
		readonly HarmonyMethod containerAttributes;
#endif

		MethodBase original;
		HarmonyMethod prefix;
		HarmonyMethod postfix;
		HarmonyMethod transpiler;

#if ALLOW_ATTRIBUTES
		public PatchProcessor(HarmonyInstance instance, Type type, HarmonyMethod attributes)
		{
			this.instance = instance;
			container = type;
			containerAttributes = attributes ?? new HarmonyMethod(null);
			prefix = containerAttributes.Clone();
			postfix = containerAttributes.Clone();
			transpiler = containerAttributes.Clone();
			ProcessType();
		}
#endif

        /// <summary>
        /// Initializes a new instance of the <see cref="PatchProcessor"/> class.
        /// </summary>
        /// <param name="instance">HarmonyInstance或者string.</param>
        /// <param name="original">目标.</param>
        /// <param name="prefix">Prolog调用例程（返回类型void或bool, 返回true调用原始例程，返回false跳过）.</param>
        /// <param name="postfix">Epilog调用例程（返回类型void）.</param>
        /// <param name="transpiler">指令重写例程
        /// <para>
        /// IEnumerable&lt;CodeInstruction&gt; TranspilerProtoType(IEnumerable&lt;CodeInstruction&gt; instructions)
        /// </para>
        /// </param>
        public PatchProcessor(object instance, MethodBase original, HarmonyMethod prefix, HarmonyMethod postfix, HarmonyMethod transpiler = null)
		{
            this.instance = null;
            string name = Guid.NewGuid().ToString("N").ToUpper();
            if (instance == null)
            {
            }
            else if(instance is HarmonyInstance)
            {
                this.instance = (HarmonyInstance)instance;
            }
            else if(instance is string)
            {
                name = (string)instance;
            }
            else
            {
            }

            if(this.instance == null)
            {
                this.instance = HarmonyInstance.Create(name);
            }

            this.original = original;
			this.prefix = prefix ?? new HarmonyMethod(null);
			this.postfix = postfix ?? new HarmonyMethod(null);
			this.transpiler = transpiler ?? new HarmonyMethod(null);
		}

#if RECORD_PATCH_STATE
		public static Patches IsPatched(MethodBase method)
		{
			var patchInfo = HarmonySharedState.GetPatchInfo(method);
			if (patchInfo == null) return null;
			return new Patches(patchInfo.prefixes, patchInfo.postfixes, patchInfo.transpilers);
		}

		public static IEnumerable<MethodBase> AllPatchedMethods()
		{
			return HarmonySharedState.GetPatchedMethods();
		}
#endif

        /// <summary>
        /// 执行Patch
        /// </summary>
        /// <returns></returns>
        public PatchInfoData Patch(PatchFlags flags)
		{
            PatchInfo patchInfo = null;

            lock (locker)
			{
#if RECORD_PATCH_STATE
				patchInfo = HarmonySharedState.GetPatchInfo(original);
                if (patchInfo == null) patchInfo = new PatchInfo();
#endif
                patchInfo = new PatchInfo();

                PatchFunctions.AddPrefix(patchInfo, instance.id, prefix);
				PatchFunctions.AddPostfix(patchInfo, instance.id, postfix);
				PatchFunctions.AddTranspiler(patchInfo, instance.id, transpiler);
				PatchFunctions.UpdateWrapper(original, patchInfo, flags);
#if RECORD_PATCH_STATE
				HarmonySharedState.UpdatePatchInfo(original, patchInfo);
#endif
			}

            return patchInfo.patchdata;
		}

#if ALLOW_ATTRIBUTES
		bool CallPrepare()
		{
			if (original != null)
				return RunMethod<HarmonyPrepare, bool>(true, original);
			return RunMethod<HarmonyPrepare, bool>(true);
		}

		void ProcessType()
		{
			original = GetOriginalMethod();

			var patchable = CallPrepare();
			if (patchable)
			{
				if (original == null)
					original = RunMethod<HarmonyTargetMethod, MethodBase>(null);
				if (original == null)
					throw new ArgumentException("No target method specified for class " + container.FullName);

				PatchTools.GetPatches(container, original, out prefix.method, out postfix.method, out transpiler.method);

				if (prefix.method != null)
				{
					if (prefix.method.IsStatic == false)
						throw new ArgumentException("Patch method " + prefix.method.Name + " in " + prefix.method.DeclaringType + " must be static");

					var prefixAttributes = prefix.method.GetHarmonyMethods();
					containerAttributes.Merge(HarmonyMethod.Merge(prefixAttributes)).CopyTo(prefix);
				}

				if (postfix.method != null)
				{
					if (postfix.method.IsStatic == false)
						throw new ArgumentException("Patch method " + postfix.method.Name + " in " + postfix.method.DeclaringType + " must be static");

					var postfixAttributes = postfix.method.GetHarmonyMethods();
					containerAttributes.Merge(HarmonyMethod.Merge(postfixAttributes)).CopyTo(postfix);
				}

				if (transpiler.method != null)
				{
					if (transpiler.method.IsStatic == false)
						throw new ArgumentException("Patch method " + transpiler.method.Name + " in " + transpiler.method.DeclaringType + " must be static");

					var infixAttributes = transpiler.method.GetHarmonyMethods();
					containerAttributes.Merge(HarmonyMethod.Merge(infixAttributes)).CopyTo(transpiler);
				}
			}
		}

		MethodBase GetOriginalMethod()
		{
			var attr = containerAttributes;
			if (attr.originalType == null) return null;
			if (attr.methodName == null)
				return AccessTools.Constructor(attr.originalType, attr.parameter);
			return AccessTools.Method(attr.originalType, attr.methodName, attr.parameter);
		}

		T RunMethod<S, T>(T defaultIfNotExisting, params object[] parameters)
		{
			var methodName = typeof(S).Name.Replace("Harmony", "");

			var paramList = new List<object> { instance };
			paramList.AddRange(parameters);
			var paramTypes = AccessTools.GetTypes(paramList.ToArray());
			var method = PatchTools.GetPatchMethod<S>(container, methodName, paramTypes);
			if (method != null && typeof(T).IsAssignableFrom(method.ReturnType))
				return (T)method.Invoke(null, paramList.ToArray());

			method = PatchTools.GetPatchMethod<S>(container, methodName, new Type[] { typeof(HarmonyInstance) });
			if (method != null && typeof(T).IsAssignableFrom(method.ReturnType))
				return (T)method.Invoke(null, new object[] { instance });

			method = PatchTools.GetPatchMethod<S>(container, methodName, Type.EmptyTypes);
			if (method != null)
			{
				if (typeof(T).IsAssignableFrom(method.ReturnType))
					return (T)method.Invoke(null, Type.EmptyTypes);

				method.Invoke(null, Type.EmptyTypes);
				return defaultIfNotExisting;
			}

			return defaultIfNotExisting;
		}
#endif

    }
}