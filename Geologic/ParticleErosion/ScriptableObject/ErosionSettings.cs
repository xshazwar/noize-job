using UnityEngine;

namespace xshazwar.noize.geologic {
    [CreateAssetMenu(fileName = "ErosionSettings", menuName = "Noize/ScriptableObjects/ErosionSettings", order = 2)]
    public class ErosionSettings : ScriptableObject {

        [Header("Particle Cycle")]
        public int CYCLES;
        public int PARTICLES_PER_CYCLE;
        public ErosionMode BEHAVIOR;
        
        [Space(10)]
        [Header("Particle Behavior")]
        [Space(3)]
        [Header("Capacity / Rate")]
        public float CAPACITY;
        public float EROSION;
        public float DEPOSITION;
        [Space(3)]
        [Header("Force Coefficients")]
        public float INERTIA;
        public float GRAVITY;
        public float DRAG;
        public float FRICTION;
        [Header("Pathfinding")]
        public float FLOW_HEIGHT_CONTRIBUTION;
        [Space(3)]
        [Header("Particle Death Conditions")]
        public float SLOW_CULL_ANGLE;
        public float SLOW_CULL_SPEED;
        public int MAXAGE;
        public float EVAP;

        [Space(10)]
        [Header("Water Behavior")]
        public int WATER_STEPS;
        [Space(3)]
        [Header("Deposition")]
        public float POOL_PLACEMENT_MULTIPLIER;
        public float TRACK_PLACEMENT_MULTIPLIER;
        [Space(3)]
        [Header("Evaporation")]
        public float SURFACE_EVAPORATION_RATE;
        public float FLOW_LOSS_RATE = 0.05f;
        
        [Space(10)]
        [Header("Soil Deposition Behavior")]
        public int PILING_RADIUS;
        public float MIN_PILE_INCREMENT;
        public float PILE_THRESHOLD; // in meters

        [Space(10)]
        [Header("Thermal Erosion")]
        public bool ENABLE_THERMAL;
        public float TALUS;
        public float THERMAL_STEP;
        public int THERMAL_CYCLES;

        void Reset()
        {
            CYCLES = 3;
            PARTICLES_PER_CYCLE = 1000;
            BEHAVIOR = ErosionMode.ALL_EROSION;
            
            INERTIA = 0.5f;
            GRAVITY = 1f;
            DRAG = 0.001f;
            FRICTION = 0.01f;
            EVAP = 0.01f;
            EROSION = 1.0f;
            DEPOSITION = 0.1f;
            FLOW_HEIGHT_CONTRIBUTION = 25f;

            SLOW_CULL_ANGLE = 3f;
            SLOW_CULL_SPEED = 0.11f;
            CAPACITY = 3f;
            MAXAGE = 100;

            WATER_STEPS = 10;
            SURFACE_EVAPORATION_RATE = 0.1f;
            POOL_PLACEMENT_MULTIPLIER = 0.5f;
            TRACK_PLACEMENT_MULTIPLIER = 80f;
            FLOW_LOSS_RATE = 0.05f;

            PILING_RADIUS = 15;
            MIN_PILE_INCREMENT = 1f;
            PILE_THRESHOLD = 2f;

            ENABLE_THERMAL = true;
            TALUS = 55f;
            THERMAL_STEP = .6f;
            THERMAL_CYCLES = 1;
        }

        public ErosionParameters AsParameters(){
            return new ErosionParameters()
            {
                INERTIA = this.INERTIA,
                GRAVITY = this.GRAVITY,
                FRICTION = this.FRICTION,
                DRAG = this.DRAG,
                EVAP = this.EVAP,
                EROSION = this.EROSION,
                DEPOSITION = this.DEPOSITION,
                FLOW_HEIGHT_CONTRIBUTION = this.FLOW_HEIGHT_CONTRIBUTION,

                SLOW_CULL_ANGLE = this.SLOW_CULL_ANGLE,
                SLOW_CULL_SPEED = this.SLOW_CULL_SPEED,
                CAPACITY = BEHAVIOR == ErosionMode.ALL_EROSION ? this.CAPACITY: 0,
                MAXAGE = this.MAXAGE,
                TERMINAL_VELOCITY = 1f / this.DRAG,

                SURFACE_EVAPORATION_RATE = this.SURFACE_EVAPORATION_RATE,
                POOL_PLACEMENT_MULTIPLIER = BEHAVIOR == ErosionMode.ONLY_THERMAL_EROSION ? 0f : this.POOL_PLACEMENT_MULTIPLIER,
                TRACK_PLACEMENT_MULTIPLIER = this.TRACK_PLACEMENT_MULTIPLIER,
                FLOW_LOSS_RATE = this.FLOW_LOSS_RATE,

                PILING_RADIUS = this.PILING_RADIUS,
                MIN_PILE_INCREMENT = this.MIN_PILE_INCREMENT,
                PILE_THRESHOLD = this.PILE_THRESHOLD
            };
        }
    }

    
}