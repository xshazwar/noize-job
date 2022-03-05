using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

// from: https://github.com/jitbit/PropMapper

// MIT License
// Copyright (c) 2019 Jitbit (the company behind "Jitbit Helpdesk" software)

// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:

// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.

// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

namespace xshazwar.utils
{

	//clones object public properties to another object
	//uses Expressions (compiled and saved to static) - faster than Reflection
	//(compilation happens with every new generic type call cause it's a new static class each time)
	public static class PropMapper<TInput, TOutput>
	{
		private static readonly Func<TInput, TOutput> _cloner;
		private static readonly Action<TInput, TOutput> _copier;

		private static readonly IEnumerable<PropertyInfo> _sourceProperties;
		private static readonly IEnumerable<PropertyInfo> _destinationProperties;

		static PropMapper()
		{
			_destinationProperties = typeof(TOutput)
				.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(prop => prop.CanWrite);
			_sourceProperties = typeof(TInput)
				.GetProperties(BindingFlags.Public | BindingFlags.Instance)
				.Where(prop => prop.CanRead);

			_cloner = CreateCloner();
			_copier = CreateCopier();
		}

		private static Func<TInput, TOutput> CreateCloner()
		{
			//check if type has parameterless constructor - just in case
			if (typeof(TOutput).GetConstructor(Type.EmptyTypes) == null) return ((x) => default(TOutput));

			var input = Expression.Parameter(typeof(TInput), "input");

			// For each property that exists in the destination object, is there a property with the same name in the source object?
			var memberBindings = _sourceProperties.Join(_destinationProperties,
				sourceProperty => sourceProperty.Name,
				destinationProperty => destinationProperty.Name,
				(sourceProperty, destinationProperty) =>
					(MemberBinding)Expression.Bind(destinationProperty,
						Expression.Property(input, sourceProperty)));

			var body = Expression.MemberInit(Expression.New(typeof(TOutput)), memberBindings);
			var lambda = Expression.Lambda<Func<TInput, TOutput>>(body, input);
			return lambda.Compile();
		}

		private static Action<TInput, TOutput> CreateCopier()
		{
			var input = Expression.Parameter(typeof(TInput), "input");
			var output = Expression.Parameter(typeof(TOutput), "output");

			// For each property that exists in the destination object, is there a property with the same name in the source object?
			var memberAssignments = _sourceProperties.Join(_destinationProperties,
				sourceProperty => sourceProperty.Name,
				destinationProperty => destinationProperty.Name,
				(sourceProperty, destinationProperty) =>
					Expression.Assign(Expression.Property(output, destinationProperty),
						Expression.Property(input, sourceProperty)));

			var body = Expression.Block(memberAssignments);
			var lambda = Expression.Lambda<Action<TInput, TOutput>>(body, input, output);
			return lambda.Compile();
		}

		public static TOutput From(TInput input)
		{
			if (input == null) return default(TOutput);
			return _cloner(input);
		}

		public static void CopyTo(TInput input, TOutput output)
		{
			_copier(input, output);
		}
	}
}