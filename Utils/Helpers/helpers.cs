using System;

  namespace xshazwar.processing.cpu.mutate {
    
    static class Helpers {
        public static void Fill<T> (T[] array, int count, T value, int threshold = 32)
        {
            if (threshold <= 0)
                throw new ArgumentException("threshold");

            int current_size = 0, keep_looping_up_to = Math.Min(count, threshold);

            while (current_size < keep_looping_up_to)
                array[current_size++] = value;

            for (int at_least_half = (count + 1) >> 1; current_size < at_least_half; current_size <<= 1)
                Array.Copy(array, 0, array, current_size, current_size);

            Array.Copy(array, 0, array, current_size, count - current_size);
        }
    }
  }