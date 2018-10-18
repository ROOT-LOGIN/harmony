using Harmony.ILCopying;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Harmony
{
	public static class PatchFunctions
	{
		public static void AddPrefix(PatchInfo patchInfo, int ownerid, HarmonyMethod info)
		{
			if (info == null || info.method == null) return;

			var priority = info.prioritiy == -1 ? Priority.Normal : info.prioritiy;
			var before = info.before ?? new string[0];
			var after = info.after ?? new string[0];

			patchInfo.AddPrefix(info.method, ownerid, priority, before, after);
		}

		public static void AddPostfix(PatchInfo patchInfo, int ownerid, HarmonyMethod info)
		{
			if (info == null || info.method == null) return;

			var priority = info.prioritiy == -1 ? Priority.Normal : info.prioritiy;
			var before = info.before ?? new string[0];
			var after = info.after ?? new string[0];

			patchInfo.AddPostfix(info.method, ownerid, priority, before, after);
		}

		public static void AddTranspiler(PatchInfo patchInfo, int ownerid, HarmonyMethod info)
		{
			if (info == null || info.method == null) return;

			var priority = info.prioritiy == -1 ? Priority.Normal : info.prioritiy;
			var before = info.before ?? new string[0];
			var after = info.after ?? new string[0];

			patchInfo.AddTranspiler(info.method, ownerid, priority, before, after);
		}

		public static List<MethodInfo> GetSortedPatchMethods(MethodBase original, Patch[] patches)
		{
			return patches
				.Where(p => p.patch != null)
				.OrderBy(p => p)
				.Select(p => p.GetMethod(original))
				.ToList();
		}

		public static void UpdateWrapper(MethodBase original, PatchInfo patchInfo, PatchFlags flags)
		{
			var sortedPrefixes = GetSortedPatchMethods(original, patchInfo.prefixes);
			var sortedPostfixes = GetSortedPatchMethods(original, patchInfo.postfixes);
			var sortedTranspilers = GetSortedPatchMethods(original, patchInfo.transpilers).Select(m=>TranspilerImpl.From(m)).ToList();

			var replacement = MethodPatcher.CreatePatchedMethod(original, sortedPrefixes, sortedPostfixes, sortedTranspilers, flags);
			if (replacement == null) throw new MissingMethodException("Cannot create dynamic replacement for " + original);

            patchInfo.patchdata.orgaddress = Memory.GetMethodStart(original);
			patchInfo.patchdata.jmpaddress = Memory.GetMethodStart(replacement);
            
			Memory.WriteJump(patchInfo.patchdata.orgaddress, patchInfo.patchdata.jmpaddress, out patchInfo.patchdata.orgbytes, out patchInfo.patchdata.jmpbytes);

			PatchTools.RememberObject(original, replacement); // no gc for new value + release old value to gc
		}
	}
}