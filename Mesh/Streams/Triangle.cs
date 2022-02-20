using System.Runtime.InteropServices;
using Unity.Mathematics;

namespace xshazwar.Meshes.Streams {

	[StructLayout(LayoutKind.Sequential)]
	public struct TriangleUInt16 {

		public ushort a, b, c;

		public static implicit operator TriangleUInt16 (int3 t) => new TriangleUInt16 {
			a = (ushort)t.x,
			b = (ushort)t.y,
			c = (ushort)t.z
		};
	}

	[StructLayout(LayoutKind.Sequential)]
	public struct TriangleUInt32 {

		public uint a, b, c;

		public static implicit operator TriangleUInt32 (int3 t) => new TriangleUInt32 {
			a = (uint)t.x,
			b = (uint)t.y,
			c = (uint)t.z
		};
	}
}