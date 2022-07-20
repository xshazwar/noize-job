using Unity.Collections;

using static Unity.Mathematics.math;

namespace xshazwar.noize.geologic {
    using Unity.Mathematics;

    struct Regression {
        float Mean(NativeArray<float> items){
            float sum = 0f;
            for( int i = 0; i < items.Length; i ++){
                sum += items[i];
            }
            return sum / items.Length;
        }

        float SumSquareDifference(NativeArray<float> items){
            float i_mean = Mean(items);
            float sum = 0f;
            for( int i = 0; i < items.Length; i ++){
                sum += pow(items[i] - i_mean, 2f);
            }
            return sum;
        }

        float ComputeSXY(NativeArray<float> xs, NativeArray<float> ys){
            float mean_x = Mean(xs);
            float mean_y = Mean(ys);
            float sum = 0f;
            for( int i = 0; i < xs.Length; i ++){
                sum += (xs[i] - mean_x) * (ys[i] - mean_y);
            }
            return sum;
        }

        float MeanSquareError(NativeArray<float> pred, NativeArray<float> real){
            float sum = 0f;
            for( int i = 0; i < pred.Length; i ++){
                sum += pow((pred[i] - real[i]), 2);
            }
            return sum / pred.Length;
        }

        float PredictLog(float x, float b1, float b2){
            return b1 + b2 * log(x);
        }

        public void LogRegression(NativeArray<float> xs, NativeArray<float> ys, out float b1, out float b2, bool RectifyToEndValue = true){
            b1 = 0f;
            b2 = 0f;
            // Convert x -> ln(x)
            int size = xs.Length;
            float xM = xs[size - 1];
            
            for( int i = 0; i < size; i ++){
                xs[i] = log(xs[i]);
            }
            
            float sxx = SumSquareDifference(xs);
            float sxy = ComputeSXY(xs, ys);
            float syy = SumSquareDifference(ys);

            b2 = sxy / sxx;
            b1 = Mean(ys) - b2 * Mean(xs);

            if (RectifyToEndValue){
                float corr = PredictLog(xM, b1, b2) - ys[size - 1];
                b1 += corr;
            }
        }
    }
}