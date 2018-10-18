using System.Collections.Generic;
using Harmony.ILCopying;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System;
using System.Collections;

namespace Harmony
{
    public abstract class TranspilerImpl
    {
        public abstract IEnumerable Invoke(object[] argv);
        public abstract ParameterInfo[] GetParameters();

        class DelegateTranspiler : TranspilerImpl
        {
            public DelegateTranspiler(TranspileDelegate transpiler)
            {
                this.transpiler = transpiler;
            }

            readonly TranspileDelegate transpiler;

            public override IEnumerable Invoke(object[] argv)
            {
                return transpiler((IEnumerable<CodeInstruction>)argv[0]);
            }
            public override ParameterInfo[] GetParameters()
            {
                return typeof(TranspilerImpl).GetMethod("Invoke").GetParameters();
            }
        }

        public static TranspilerImpl From(TranspileDelegate transpiler)
        {
            return new DelegateTranspiler(transpiler);
        }

        class MethodInfoTranspiler : TranspilerImpl
        {
            public MethodInfoTranspiler(MethodInfo transpiler)
            {
                this.transpiler = transpiler;
            }

            readonly MethodInfo transpiler;

            public override IEnumerable Invoke(object[] argv)
            {
                return transpiler.Invoke(null, argv) as IEnumerable;
            }
            public override ParameterInfo[] GetParameters()
            {
                return transpiler.GetParameters();
            }
        }

        public static TranspilerImpl From(MethodInfo transpiler)
        {
            return new MethodInfoTranspiler(transpiler);
        }

        public static implicit operator TranspilerImpl(MethodInfo transpiler)
        {
            return From(transpiler);
        }

        public static implicit operator TranspilerImpl(TranspileDelegate transpiler)
        {
            return From(transpiler);
        }
    }

    public class CodeTranspiler
	{        
        private IEnumerable<CodeInstruction> codeInstructions;
		private List<TranspilerImpl> transpilers = new List<TranspilerImpl>();

		public CodeTranspiler(List<ILInstruction> ilInstructions)
		{
			codeInstructions = ilInstructions
				.Select(ilInstruction => ilInstruction.GetCodeInstruction())
				.ToList().AsEnumerable();
		}

		public void Add(TranspilerImpl transpiler)
		{
			transpilers.Add(transpiler);
		}

        private static IEnumerable ConvertInstructions(Type type, IEnumerable enumerable)
		{
			var enumerableAssembly = type.GetGenericTypeDefinition().Assembly;
			var genericListType = enumerableAssembly.GetType(typeof(List<>).FullName);
			var elementType = type.GetGenericArguments()[0];
			var listType = enumerableAssembly.GetType(genericListType.MakeGenericType(new Type[] { elementType }).FullName);
			var list = Activator.CreateInstance(listType);
			var listAdd = list.GetType().GetMethod("Add");

			foreach (var op in enumerable)
			{
				var elementTo = Activator.CreateInstance(elementType, new object[] { OpCodes.Nop, null });
				Traverse.IterateFields(op, elementTo, (trvFrom, trvDest) => trvDest.SetValue(trvFrom.GetValue()));
				listAdd.Invoke(list, new object[] { elementTo });
			}
			return list as IEnumerable;
		}

		private static IEnumerable ConvertInstructions(TranspilerImpl transpiler, IEnumerable enumerable)
		{
			/*var type = transpiler.GetParameters()
				  .Select(p => p.ParameterType)
				  .FirstOrDefault(t => t.IsGenericType && t.GetGenericTypeDefinition().Name.StartsWith("IEnumerable"));*/
			return ConvertInstructions(typeof(IEnumerable<CodeInstruction>), enumerable);
		}

		public IEnumerable<CodeInstruction> GetResult(ILGenerator generator, MethodBase method)
		{
			IEnumerable instructions = codeInstructions;
			transpilers.ForEach(transpiler =>
			{
				instructions = ConvertInstructions(transpiler, instructions);
				var parameter = new List<object>();
				transpiler.GetParameters().Select(param => param.ParameterType).Do(type =>
				{
					if (type.IsAssignableFrom(typeof(ILGenerator)))
						parameter.Add(generator);
					else if (type.IsAssignableFrom(typeof(MethodBase)))
						parameter.Add(method);
					else
						parameter.Add(instructions);
				});
				instructions = transpiler.Invoke(parameter.ToArray());
			});
			instructions = ConvertInstructions(typeof(IEnumerable<CodeInstruction>), instructions);
			return instructions as IEnumerable<CodeInstruction>;
		}
	}
}