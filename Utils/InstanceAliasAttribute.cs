using UnityEngine;
 
 namespace xshazwar.noize.utils {
    public class InstanceAliasAttribute : PropertyAttribute
    {
        public string namePrefix { get ; private set; }    
        public InstanceAliasAttribute( string name )
        {
            namePrefix = name ;
        }
    }
}