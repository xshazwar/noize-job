using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using Unity.IO.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;

namespace xshazwar.noize.pipeline {

    [Serializable]
    internal struct FileDirectory {
        public string alias;
        public string version;
        public List<FileObject> files;
        [NonSerialized]
        private Dictionary<string, int> lookup;
        [NonSerialized]
        public string fullPath;

        public static FileDirectory FromFile(string basePath, string alias = "", string version = ""){
            string fullPath = Path.Combine(basePath, $"save__{alias}", "files.json");
            Debug.Log($"file directory path for {alias} >> {fullPath}");
            FileDirectory fd;
            if(File.Exists(fullPath)){
                fd = JsonUtility.FromJson<FileDirectory>(File.ReadAllText(fullPath));
                fd.fullPath = fullPath;
            }else{
                fd = new FileDirectory { 
                    alias = alias,
                    version = version,
                    files = new List<FileObject>(),
                    fullPath = fullPath
                };
            }
            fd.Init();
            return fd;
        }

        public void Init(){
            lookup = new Dictionary<string, int>();
            for(int i = 0; i < files.Count; i++){
                FileObject f = files[i];
                string name_ = $"{f.id}_{f.type}";
                lookup[name_] = i;
            }
        
        }

        public int GetCount(string name, string type){
            string name_ = $"{name}_{type}";
            if(lookup.ContainsKey(name_)){
                int idx = lookup[name_];
                return files[idx].size;
            }else{
                return -1;
            }
            
        }

        public void FlushToDisk(){
            new System.IO.FileInfo(fullPath).Directory.Create();
            File.WriteAllText(fullPath, JsonUtility.ToJson(this));
        }

        public void SetCount(string name, string type, int size){
            string name_ = $"{name}_{type}";
            if(lookup.ContainsKey(name_)){
                int idx = lookup[name_];
                FileObject f = files[idx];
                f.size = size;
                files[idx] = f;
            }else{
                files.Add(
                    new FileObject {
                        id = name,
                        type = type,
                        size = size
                    }
                );
                lookup[name_] = files.Count- 1;
            }
            FlushToDisk();
        }
    }


    [Serializable]
    internal struct FileObject {
        public string id;
        public string type;
        public int size;
    }
    
    internal unsafe class BinaryIO {
        
        private Stream stream;
        private byte[] buffer;
        public long Position
        {
            get => stream.Position;
            set => stream.Position = value;
        }

        public BinaryIO(int bufferSize = 65536){
            buffer = new byte[bufferSize];
        }

        public void SetFileWrite(string name){
            new System.IO.FileInfo(name).Directory.Create();
            stream = File.Open(name, FileMode.Create, FileAccess.Write);
        }

        public bool SetFileRead(string name){
            if(!File.Exists(name)){
                return false;
            }
            stream = File.Open(name, FileMode.Open, FileAccess.Read, FileShare.Read);
            return true;
        }

        public void Dispose(){
            stream.Dispose();
        }

        public void WriteBytes(void* data, int bytes)
        {
            int remaining = bytes;
            int bufferSize = buffer.Length;

            fixed (byte* fixedBuffer = buffer)
            {
                while (remaining != 0)
                {
                    int bytesToWrite = Math.Min(remaining, bufferSize);
                    UnsafeUtility.MemCpy(fixedBuffer, data, bytesToWrite);
                    stream.Write(buffer, 0, bytesToWrite);
                    data = (byte*) data + bytesToWrite;
                    remaining -= bytesToWrite;
                }
            }
        }

        public void ReadBytes(void* data, int bytes)
        {
            int remaining = bytes;
            int bufferSize = buffer.Length;

            fixed(byte* fixedBuffer = buffer)
            {
                while (remaining != 0)
                {
                    int read = stream.Read(buffer, 0, Math.Min(remaining, bufferSize));
                    remaining -= read;
                    UnsafeUtility.MemCpy(data, fixedBuffer, read);
                    data = (byte*)data + read;
                }
            }
        }

        public void ReadArray<T>(NativeArray<T> data, int count) where T: struct
        {
            ReadBytes((byte*)data.GetUnsafeReadOnlyPtr(), count * UnsafeUtility.SizeOf<T>());
        }

        public void ReadList<T>(NativeArray<T> data, int count) where T: struct
        {
            ReadBytes((byte*)data.GetUnsafeReadOnlyPtr(), count * UnsafeUtility.SizeOf<T>());
        }

        public void WriteArray<T>(NativeArray<T> data) where T: struct{
            WriteBytes(data.GetUnsafeReadOnlyPtr(), data.Length * UnsafeUtility.SizeOf<T>());
        }

        public void WriteList<T>(NativeArray<T> data) where T: struct{
            WriteBytes(data.GetUnsafeReadOnlyPtr(), data.Length * UnsafeUtility.SizeOf<T>());
        }

    }

    public unsafe class PipelineSerdeManager {

        BinaryIO serde;
        string basePath;
        string alias;
        string version;
        FileDirectory directory;

        public PipelineSerdeManager(string path, string alias, string version){
            serde = new BinaryIO();
            this.alias = alias;
            this.version = version;
            SetPath(path);
            directory = FileDirectory.FromFile(path, alias, version);
        }

        public void SetPath(string path){
            basePath = path;
        }

        private string CleanFileName(string name){
            var invalids = System.IO.Path.GetInvalidFileNameChars();
            return String.Join("_", name.Split(invalids, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
        }

        public string GetFQN(string name){
            return Path.Combine(basePath, $"save__{alias}", "data", $"{CleanFileName(name)}.data");
        }

        public unsafe void WriteData<T>(void* data, int bytes, string name, int size) where T: struct {
            serde.SetFileWrite(GetFQN(name));
            serde.WriteBytes(data, bytes);
            serde.Dispose();
            directory.SetCount(name, $"{typeof(T).Name}", size);
        }

        public unsafe void ReadData(void* target, int bytes, string name){
            if(!serde.SetFileRead(GetFQN(name))){
                Debug.Log($"No current file for {name}");
                return;
            };
            serde.ReadBytes(target, bytes);
            serde.Dispose();
        }

        public int CachedSize<T>(string name) where T: struct {
            int r = directory.GetCount(name, $"{typeof(T).Name}");
            Debug.Log($"Saved file query for: {name} >> {typeof(T).Name} >> {r > 0}");
            return r;
            
        }
    }
}