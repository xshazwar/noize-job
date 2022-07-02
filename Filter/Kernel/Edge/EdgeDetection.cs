using System;
using System.Collections.Generic;

namespace xshazwar.noize.filter.edge {
    
    public enum EdgeType1D {
        SOBEL_HORIZONTAL,
        SOBEL_VERTICAL,
        PREWITT_HORIZONTAL,
        PREWITT_VERTICAL,
    }

    public enum EdgeDirection{
        HORIZONTAL,
        VERTICAL
    }

    public enum EdgeAlgorithm {
        SOBEL,
        PREWITT
    }
    public static class EdgeDetectionKernel {
        public static List<float[]> Get1DKernel(EdgeAlgorithm algo, EdgeDirection dir){
            if (algo == EdgeAlgorithm.SOBEL && dir == EdgeDirection.HORIZONTAL){
                    return Set1D(EdgeType1D.SOBEL_HORIZONTAL);
            }
            if (algo == EdgeAlgorithm.SOBEL && dir == EdgeDirection.VERTICAL){
                    return Set1D(EdgeType1D.SOBEL_VERTICAL);
            }
            if (algo == EdgeAlgorithm.PREWITT && dir == EdgeDirection.HORIZONTAL){
                    return Set1D(EdgeType1D.PREWITT_HORIZONTAL);
            }
            if (algo == EdgeAlgorithm.PREWITT && dir == EdgeDirection.VERTICAL){
                    return Set1D(EdgeType1D.PREWITT_VERTICAL);
            }
            throw new ArgumentOutOfRangeException();
        }

        public static List<float[]> Get2DKernel(EdgeAlgorithm algo){
            List<float[]> kernel = Get1DKernel(algo, EdgeDirection.HORIZONTAL);
            kernel.AddRange(Get1DKernel(algo, EdgeDirection.VERTICAL));
            return kernel;
        }
        static List<float[]> Set1D(EdgeType1D type){
            return new List<float[]>(){
                kernel_1d_x[type],
                kernel_1d_z[type]
            };
        }
        private static Dictionary<EdgeType1D, float[]> kernel_1d_x = new Dictionary<EdgeType1D, float[]>() {
            {EdgeType1D.SOBEL_HORIZONTAL, new float[] {-1f, 0f, 1f}},
            {EdgeType1D.SOBEL_VERTICAL, new float [] {1f, 2f, 1f}},
            {EdgeType1D.PREWITT_HORIZONTAL, new float [] {1f, 0f, -1f}},
            {EdgeType1D.PREWITT_VERTICAL, new float [] {1f, 1f, 1f}}
        };

        private static Dictionary<EdgeType1D, float[]> kernel_1d_z = new Dictionary<EdgeType1D, float[]>() {
            {
                EdgeType1D.SOBEL_HORIZONTAL, new float[] {
                    1f,
                    2f,
                    1f
                }
            },
            {
                EdgeType1D.SOBEL_VERTICAL, new float [] {
                    1f, 
                    0f,
                    -1f
                }
            },
            {EdgeType1D.PREWITT_HORIZONTAL, new float [] {
                    1f,
                    1f,
                    1f
                }
            },
            {EdgeType1D.PREWITT_VERTICAL, new float [] {
                    -1f, 
                    0f,
                    1f
                }
            }
        };
    }
}