using System;

namespace GLTF.Math
{
	// class is naively implemented
	public class Matrix4x4 : IEquatable<Matrix4x4>
	{
		public static readonly Matrix4x4 Identity = new Matrix4x4(
			1f, 0f, 0f, 0f,
			0f, 1f, 0f, 0f,
			0f, 0f, 1f, 0f,
			0f, 0f, 0f, 1f
			);

		/// <summary>
		/// Matrix is column major ordered
		/// </summary>
		public Matrix4x4(float m11, float m21, float m31, float m41, float m12, float m22, float m32, float m42, float m13, float m23, float m33, float m43, float m14, float m24, float m34, float m44)
		{
			M11 = m11;
			M12 = m12;
			M13 = m13;
			M14 = m14;
			M21 = m21;
			M22 = m22;
			M23 = m23;
			M24 = m24;
			M31 = m31;
			M32 = m32;
			M33 = m33;
			M34 = m34;
			M41 = m41;
			M42 = m42;
			M43 = m43;
			M44 = m44;
		}
		public Matrix4x4(double m11, double m21, double m31, double m41, double m12, double m22, double m32, double m42, double m13, double m23, double m33, double m43, double m14, double m24, double m34, double m44)
		{
			M11d = m11;
			M12d = m12;
			M13d = m13;
			M14d = m14;
			M21d = m21;
			M22d = m22;
			M23d = m23;
			M24d = m24;
			M31d = m31;
			M32d = m32;
			M33d = m33;
			M34d = m34;
			M41d = m41;
			M42d = m42;
			M43d = m43;
			M44d = m44;
		}

		public Matrix4x4(Matrix4x4 other)
		{
			Array.Copy(other.mat, 0, mat, 0, 16);
		}

		private double[] mat = new double[16];

		public float M11 { get { return (float)mat[0]; } set { mat[0] = value; } }
		public float M21 { get { return (float)mat[1]; } set { mat[1] = value; } }
		public float M31 { get { return (float)mat[2]; } set { mat[2] = value; } }
		public float M41 { get { return (float)mat[3]; } set { mat[3] = value; } }
		public float M12 { get { return (float)mat[4]; } set { mat[4] = value; } }
		public float M22 { get { return (float)mat[5]; } set { mat[5] = value; } }
		public float M32 { get { return (float)mat[6]; } set { mat[6] = value; } }
		public float M42 { get { return (float)mat[7]; } set { mat[7] = value; } }
		public float M13 { get { return (float)mat[8]; } set { mat[8] = value; } }
		public float M23 { get { return (float)mat[9]; } set { mat[9] = value; } }
		public float M33 { get { return (float)mat[10]; } set { mat[10] = value; } }
		public float M43 { get { return (float)mat[11]; } set { mat[11] = value; } }
		public float M14 { get { return (float)mat[12]; } set { mat[12] = value; } }
		public float M24 { get { return (float)mat[13]; } set { mat[13] = value; } }
		public float M34 { get { return (float)mat[14]; } set { mat[14] = value; } }
		public float M44 { get { return (float)mat[15]; } set { mat[15] = value; } }

        public double M11d { get { return mat[0]; } set { mat[0] = value; } }
        public double M21d { get { return mat[1]; } set { mat[1] = value; } }
        public double M31d { get { return mat[2]; } set { mat[2] = value; } }
        public double M41d { get { return mat[3]; } set { mat[3] = value; } }
        public double M12d { get { return mat[4]; } set { mat[4] = value; } }
        public double M22d { get { return mat[5]; } set { mat[5] = value; } }
        public double M32d { get { return mat[6]; } set { mat[6] = value; } }
        public double M42d { get { return mat[7]; } set { mat[7] = value; } }
        public double M13d { get { return mat[8]; } set { mat[8] = value; } }
        public double M23d { get { return mat[9]; } set { mat[9] = value; } }
        public double M33d { get { return mat[10]; } set { mat[10] = value; } }
        public double M43d { get { return mat[11]; } set { mat[11] = value; } }
        public double M14d { get { return mat[12]; } set { mat[12] = value; } }
        public double M24d { get { return mat[13]; } set { mat[13] = value; } }
        public double M34d { get { return mat[14]; } set { mat[14] = value; } }
        public double M44d { get { return mat[15]; } set { mat[15] = value; } }

        public bool Equals(Matrix4x4 other)
		{
			if (ReferenceEquals(null, other)) return false;
			if (ReferenceEquals(this, other)) return true;

			return M11d == other.M11d && M12d == other.M12d && M13d == other.M13d && M14d == other.M14d &&
				   M21d == other.M21d && M22d == other.M22d && M23d == other.M23d && M24d == other.M24d &&
				   M31d == other.M31d && M32d == other.M32d && M33d == other.M33d && M34d == other.M34d &&
				   M41d == other.M41d && M42d == other.M42d && M43d == other.M43d && M44d == other.M44d;
		}

		public override bool Equals(object obj)
		{
			if (ReferenceEquals(null, obj)) return false;
			if (ReferenceEquals(this, obj)) return true;
			if (obj.GetType() != this.GetType()) return false;
			return Equals((Matrix4x4) obj);
		}

		public override int GetHashCode()
		{
			return (mat != null ? mat.GetHashCode() : 0);
		}

		public void SetValue(int index, float value)
        {
            SetValue(index, (double)value);
        }
		public void SetValue(int index, double value)
		{
			if(index > mat.Length)
			{
				throw new IndexOutOfRangeException("Index " + index + " is out of range for a 4x4 matrix.");				
			}

			mat[index] = value;
		}


		public void SetValue(int row, int column, float value)
        {
            SetValue(row, column, (double)value);
        }
		public void SetValue(int row, int column, double value)
		{
			switch(row)
			{
				case 0:
					switch (column)
					{
						case 0:
							M11d = value;
							break;
						case 1:
							M12d = value;
							break;
						case 2:
							M13d = value;
							break;
						case 3:
							M14d = value;
							break;
						default:
							throw new IndexOutOfRangeException("Column " + column + " is out of range for a 4x4 matrix.");
					}

					break;
				case 1:
					switch (column)
					{
						case 0:
							M21d = value;
							break;
						case 1:
							M22d = value;
							break;
						case 2:
							M23d = value;
							break;
						case 3:
							M24d = value;
							break;
						default:
							throw new IndexOutOfRangeException("Column " + column + " is out of range for a 4x4 matrix.");
					}

					break;
				case 2:
					switch (column)
					{
						case 0:
							M31d = value;
							break;
						case 1:
							M32d = value;
							break;
						case 2:
							M33d = value;
							break;
						case 3:
							M34d = value;
							break;
						default:
							throw new IndexOutOfRangeException("Column " + column + " is out of range for a 4x4 matrix.");
					}

					break;
				case 3:
					switch (column)
					{
						case 0:
							M41d = value;
							break;
						case 1:
							M42d = value;
							break;
						case 2:
							M43d = value;
							break;
						case 3:
							M44d = value;
							break;
						default:
							throw new IndexOutOfRangeException("Column " + column + " is out of range for a 4x4 matrix.");
					}

					break;
				default:
					throw new IndexOutOfRangeException("Row " + row + " is out of range for a 4x4 matrix.");
			}
		}
	}
}
