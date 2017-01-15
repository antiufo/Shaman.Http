using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Shaman.Runtime
{
    class NakedStringBuilder
    {
        internal int Length;
        internal char[] Data;

        public NakedStringBuilder(int capacity)
        {
            Data = new char[capacity];
        }

        public NakedStringBuilder(string v, int capacity)
        {
            Data = new char[capacity];
            Append(v);
        }

        public void EnsureCapacity(int capacity)
        {
            if (Data.Length < capacity)
            {
                var n = new char[Math.Max(Data.Length * 2, capacity)];
                Array.Copy(Data, 0, n, 0, Length);
                Data = n;
            }
        }

        public void Append(char ch)
        {
            EnsureCapacity(Length + 1);
            Data[Length++] = ch;
        }

        public void Append(object value)
        {
            Append(Convert.ToString(value, CultureInfo.InvariantCulture));
        }

        public void Append(string value)
        {
            if (value == null) return;
            EnsureCapacity(Length + value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                Data[Length++] = value[i];
            }
        }

        public override string ToString()
        {
            return new string(Data, 0, Length);
        }


        public int IndexOf(char ch)
        {
            for (int i = 0; i < Length; i++)
            {
                if (Data[i] == ch) return i;
            }
            return -1;
        }



    }
}
