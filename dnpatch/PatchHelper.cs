﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using dnlib.DotNet;
using dnlib.DotNet.Emit;
using dnlib.DotNet.Writer;

namespace dnpatch
{
    internal class PatchHelper
    {
        private readonly ModuleDef _module;
        private readonly string _file;
        private readonly bool _keepOldMaxStack = false;

        public PatchHelper(string file)
        {
            _file = file;
            _module = ModuleDefMD.Load(file);
        }

        public PatchHelper(string file, bool keepOldMaxStack)
        {
            _file = file;
            _module = ModuleDefMD.Load(file);
            _keepOldMaxStack = keepOldMaxStack;
        }

        public PatchHelper(ModuleDefMD module, bool keepOldMaxStack)
        {
            _module = module;
            _keepOldMaxStack = keepOldMaxStack;
        }

        public PatchHelper(ModuleDef module, bool keepOldMaxStack)
        {
            _module = module;
            _keepOldMaxStack = keepOldMaxStack;
        }

        public PatchHelper(Stream stream, bool keepOldMaxStack)
        {
            _module = ModuleDefMD.Load(stream);
            _keepOldMaxStack = keepOldMaxStack;
        }

        public  void PatchAndClear(Target target)
        {
            string[] nestedClasses = { };
            if (target.NestedClasses != null)
            {
                nestedClasses = target.NestedClasses;
            }
            else if (target.NestedClass != null)
            {
                nestedClasses = new[] { target.NestedClass };
            }
            var type = FindType(target.Namespace + "." + target.Class, nestedClasses);
            var method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            instructions.Clear();
            if (target.Instructions != null)
            {
                for (int i = 0; i < target.Instructions.Length; i++)
                {
                    instructions.Insert(i, target.Instructions[i]);
                }
            }
            else
            {
                instructions.Insert(0, target.Instruction);
            }
        }

        public  void PatchOffsets(Target target)
        {
            string[] nestedClasses = { };
            if (target.NestedClasses != null)
            {
                nestedClasses = target.NestedClasses;
            }
            else if (target.NestedClass != null)
            {
                nestedClasses = new[] { target.NestedClass };
            }
            var type = FindType(target.Namespace + "." + target.Class, nestedClasses);
            var method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Indices != null && target.Instructions != null)
            {
                for (int i = 0; i < target.Indices.Length; i++)
                {
                    instructions[target.Indices[i]] = target.Instructions[i];
                }
            }
            else if (target.Index != -1 && target.Instruction != null)
            {
                instructions[target.Index] = target.Instruction;
            }
            else if (target.Index == -1)
            {
                throw new Exception("No index specified");
            }
            else if (target.Instruction == null)
            {
                throw new Exception("No instruction specified");
            }
            else if (target.Indices == null)
            {
                throw new Exception("No Indices specified");
            }
            else if (target.Instructions == null)
            {
                throw new Exception("No instructions specified");
            }
        }

        public  TypeDef FindType(string classPath, string[] nestedClasses)
        {
            if (classPath.First() == '.')
                classPath = classPath.Remove(0, 1);
            foreach (var module in _module.Assembly.Modules)
            {
                foreach (var type in _module.Types)
                {
                    if (type.FullName == classPath)
                    {
                        TypeDef t = null;
                        if (nestedClasses != null && nestedClasses.Length > 0)
                        {
                            foreach (var nc in nestedClasses)
                            {
                                if (t == null)
                                {
                                    if (!type.HasNestedTypes) continue;
                                    foreach (var typeN in type.NestedTypes)
                                    {
                                        if (typeN.Name == nc)
                                        {
                                            t = typeN;
                                        }
                                    }
                                }
                                else
                                {
                                    if (!t.HasNestedTypes) continue;
                                    foreach (var typeN in t.NestedTypes)
                                    {
                                        if (typeN.Name == nc)
                                        {
                                            t = typeN;
                                        }
                                    }
                                }
                            }
                        }
                        else
                        {
                            t = type;
                        }
                        return t;
                    }
                }
            }
            return null;
        }

        public  MethodDef FindMethod(TypeDef type, string methodName, string[] parameters, string returnType)
        {
            bool checkParams = parameters != null;
            foreach (var m in type.Methods)
            {
                bool isMethod = true;
                if (checkParams && parameters.Length != m.Parameters.Count) continue;
                if (methodName != m.Name) continue;
                if (!string.IsNullOrEmpty(returnType) && returnType != m.ReturnType.TypeName) continue;
                if (checkParams)
                {
                    if (m.Parameters.Where((param, i) => param.Type.TypeName != parameters[i]).Any())
                    {
                        isMethod = false;
                    }
                }
                if(isMethod) return m;
            }
            return null;
        }

        public  Target FixTarget(Target target)
        {
            target.Indices = new int[] { };
            target.Index = -1;
            target.Instruction = null;
            return target;
        }

        public  void Save(string name)
        {
            if (_keepOldMaxStack)
                _module.Write(name, new ModuleWriterOptions(_module)
                {
                    MetaDataOptions = {Flags = MetaDataFlags.KeepOldMaxStack}
                });
            else
                _module.Write(name);
        }

        public  void Save(bool backup)
        {
            if (string.IsNullOrEmpty(_file))
            {
                throw new Exception("Assembly/module was loaded in memory, and no file was specified. Use Save(string) method to save the patched assembly.");
            }
            if (_keepOldMaxStack)
                _module.Write(_file + ".tmp", new ModuleWriterOptions(_module)
                {
                    MetaDataOptions = { Flags = MetaDataFlags.KeepOldMaxStack }
                });
            else
                _module.Write(_file + ".tmp");
            _module.Dispose();
            if (backup)
            {
                if (File.Exists(_file + ".bak"))
                {
                    File.Delete(_file + ".bak");
                }
                File.Move(_file, _file + ".bak");
            }
            else
            {
                File.Delete(_file);
            }
            File.Move(_file + ".tmp", _file);
        }

        public Target[] FindInstructionsByOperand(string[] operand)
        {
            List<ObfuscatedTarget> obfuscatedTargets = new List<ObfuscatedTarget>();
            List<string> operands = operand.ToList();
            foreach (var type in _module.Types)
            {
                if (!type.HasNestedTypes)
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Body != null)
                        {
                            List<int> indexList = new List<int>();
                            var obfuscatedTarget = new ObfuscatedTarget()
                            {
                                Type = type,
                                Method = method
                            };
                            int i = 0;
                            foreach (var instruction in method.Body.Instructions)
                            {
                                if (instruction.Operand != null)
                                {
                                    if (operands.Contains(instruction.Operand.ToString()))
                                    {
                                        indexList.Add(i);
                                        operands.Remove(instruction.Operand.ToString());
                                    }
                                }
                                i++;
                            }
                            if (indexList.Count == operand.Length)
                            {
                                obfuscatedTarget.Indices = indexList;
                                obfuscatedTargets.Add(obfuscatedTarget);
                            }
                            operands = operand.ToList();
                        }
                    }
                }
                else
                {
                    var nestedTypes = type.NestedTypes;
                    NestedWorker:
                    foreach (var nestedType in nestedTypes)
                    {
                        foreach (var method in type.Methods)
                        {
                            if (method.Body != null)
                            {
                                List<int> indexList = new List<int>();
                                var obfuscatedTarget = new ObfuscatedTarget()
                                {
                                    Type = type,
                                    Method = method
                                };
                                int i = 0;
                                obfuscatedTarget.NestedTypes.Add(nestedType.Name);
                                foreach (var instruction in method.Body.Instructions)
                                {
                                    if (instruction.Operand != null)
                                    {
                                        if (operands.Contains(instruction.Operand.ToString()))
                                        {
                                            indexList.Add(i);
                                            operands.Remove(instruction.Operand.ToString());
                                        }
                                    }
                                    i++;
                                }
                                if (indexList.Count == operand.Length)
                                {
                                    obfuscatedTarget.Indices = indexList;
                                    obfuscatedTargets.Add(obfuscatedTarget);
                                }
                                operands = operand.ToList();
                            }
                        }
                        if (nestedType.HasNestedTypes)
                        {
                            nestedTypes = nestedType.NestedTypes;
                            goto NestedWorker;
                        }
                    }
                }
            }
            List<Target> targets = new List<Target>();
            foreach (var obfuscatedTarget in obfuscatedTargets)
            {
                Target t = new Target()
                {
                    Namespace = obfuscatedTarget.Type.Namespace,
                    Class = obfuscatedTarget.Type.Name,
                    Method = obfuscatedTarget.Method.Name,
                    NestedClasses = obfuscatedTarget.NestedTypes.ToArray()
                };
                if (obfuscatedTarget.Indices.Count == 1)
                {
                    t.Index = obfuscatedTarget.Indices[0];
                }
                else if (obfuscatedTarget.Indices.Count > 1)
                {
                    t.Indices = obfuscatedTarget.Indices.ToArray();
                }

                targets.Add(t);
            }
            return targets.ToArray();
        }

        public  Target[] FindInstructionsByOperand(int[] operand)
        {
            List<ObfuscatedTarget> obfuscatedTargets = new List<ObfuscatedTarget>();
            List<int> operands = operand.ToList();
            foreach (var type in _module.Types)
            {
                if (!type.HasNestedTypes)
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Body != null)
                        {
                            List<int> indexList = new List<int>();
                            var obfuscatedTarget = new ObfuscatedTarget()
                            {
                                Type = type,
                                Method = method
                            };
                            int i = 0;
                            foreach (var instruction in method.Body.Instructions)
                            {
                                if (instruction.Operand != null)
                                {
                                    if (operands.Contains(Convert.ToInt32(instruction.Operand.ToString())))
                                    {
                                        indexList.Add(i);
                                        operands.Remove(Convert.ToInt32(instruction.Operand.ToString()));
                                    }
                                }
                                i++;
                            }
                            if (indexList.Count == operand.Length)
                            {
                                obfuscatedTarget.Indices = indexList;
                                obfuscatedTargets.Add(obfuscatedTarget);
                            }
                            operands = operand.ToList();
                        }
                    }
                }
                else
                {
                    var nestedTypes = type.NestedTypes;
                    NestedWorker:
                    foreach (var nestedType in nestedTypes)
                    {
                        foreach (var method in type.Methods)
                        {
                            if (method.Body != null)
                            {
                                List<int> indexList = new List<int>();
                                var obfuscatedTarget = new ObfuscatedTarget()
                                {
                                    Type = type,
                                    Method = method
                                };
                                int i = 0;
                                obfuscatedTarget.NestedTypes.Add(nestedType.Name);
                                foreach (var instruction in method.Body.Instructions)
                                {
                                    if (instruction.Operand != null)
                                    {
                                        if (operands.Contains(Convert.ToInt32(instruction.Operand.ToString())))
                                        {
                                            indexList.Add(i);
                                            operands.Remove(Convert.ToInt32(instruction.Operand.ToString()));
                                        }
                                    }
                                    i++;
                                }
                                if (indexList.Count == operand.Length)
                                {
                                    obfuscatedTarget.Indices = indexList;
                                    obfuscatedTargets.Add(obfuscatedTarget);
                                }
                                operands = operand.ToList();
                            }
                        }
                        if (nestedType.HasNestedTypes)
                        {
                            nestedTypes = nestedType.NestedTypes;
                            goto NestedWorker;
                        }
                    }
                }
            }
            List<Target> targets = new List<Target>();
            foreach (var obfuscatedTarget in obfuscatedTargets)
            {
                Target t = new Target()
                {
                    Namespace = obfuscatedTarget.Type.Namespace,
                    Class = obfuscatedTarget.Type.Name,
                    Method = obfuscatedTarget.Method.Name,
                    NestedClasses = obfuscatedTarget.NestedTypes.ToArray()
                };
                if (obfuscatedTarget.Indices.Count == 1)
                {
                    t.Index = obfuscatedTarget.Indices[0];
                }
                else if (obfuscatedTarget.Indices.Count > 1)
                {
                    t.Indices = obfuscatedTarget.Indices.ToArray();
                }

                targets.Add(t);
            }
            return targets.ToArray();
        }

        public  Target[] FindInstructionsByOpcode(OpCode[] opcode)
        {
            List<ObfuscatedTarget> obfuscatedTargets = new List<ObfuscatedTarget>();
            List<string> operands = opcode.Select(o => o.Name).ToList();
            foreach (var type in _module.Types)
            {
                if (!type.HasNestedTypes)
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Body != null)
                        {
                            List<int> indexList = new List<int>();
                            var obfuscatedTarget = new ObfuscatedTarget()
                            {
                                Type = type,
                                Method = method
                            };
                            int i = 0;
                            foreach (var instruction in method.Body.Instructions)
                            {
                                if (operands.Contains(instruction.OpCode.Name))
                                {
                                    indexList.Add(i);
                                    operands.Remove(instruction.OpCode.Name);
                                }
                                i++;
                            }
                            if (indexList.Count == opcode.Length)
                            {
                                obfuscatedTarget.Indices = indexList;
                                obfuscatedTargets.Add(obfuscatedTarget);
                            }
                            operands = opcode.Select(o => o.Name).ToList();
                        }
                    }
                }
                else
                {
                    var nestedTypes = type.NestedTypes;
                    NestedWorker:
                    foreach (var nestedType in nestedTypes)
                    {
                        foreach (var method in type.Methods)
                        {
                            if (method.Body != null)
                            {
                                List<int> indexList = new List<int>();
                                var obfuscatedTarget = new ObfuscatedTarget()
                                {
                                    Type = type,
                                    Method = method
                                };
                                int i = 0;
                                obfuscatedTarget.NestedTypes.Add(nestedType.Name);
                                foreach (var instruction in method.Body.Instructions)
                                {
                                    if (operands.Contains(instruction.OpCode.Name))
                                    {
                                        indexList.Add(i);
                                        operands.Remove(instruction.OpCode.Name);
                                    }
                                    i++;
                                }
                                if (indexList.Count == opcode.Length)
                                {
                                    obfuscatedTarget.Indices = indexList;
                                    obfuscatedTargets.Add(obfuscatedTarget);
                                }
                                operands = opcode.Select(o => o.Name).ToList();
                            }
                        }
                        if (nestedType.HasNestedTypes)
                        {
                            nestedTypes = nestedType.NestedTypes;
                            goto NestedWorker;
                        }
                    }
                }
            }
            List<Target> targets = new List<Target>();
            foreach (var obfuscatedTarget in obfuscatedTargets)
            {
                Target t = new Target()
                {
                    Namespace = obfuscatedTarget.Type.Namespace,
                    Class = obfuscatedTarget.Type.Name,
                    Method = obfuscatedTarget.Method.Name,
                    NestedClasses = obfuscatedTarget.NestedTypes.ToArray()
                };
                if (obfuscatedTarget.Indices.Count == 1)
                {
                    t.Index = obfuscatedTarget.Indices[0];
                }
                else if (obfuscatedTarget.Indices.Count > 1)
                {
                    t.Indices = obfuscatedTarget.Indices.ToArray();
                }

                targets.Add(t);
            }
            return targets.ToArray();
        }

        public  Target[] FindInstructionsByOperand(Target target, int[] operand, bool removeIfFound = false)
        {
            List<ObfuscatedTarget> obfuscatedTargets = new List<ObfuscatedTarget>();
            List<int> operands = operand.ToList();
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef m = null;
            if (target.Method != null)
                m = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            if (m != null)
            {
                List<int> indexList = new List<int>();
                var obfuscatedTarget = new ObfuscatedTarget()
                {
                    Type = type,
                    Method = m
                };
                int i = 0;
                foreach (var instruction in m.Body.Instructions)
                {
                    if (instruction.Operand != null)
                    {
                        if (operands.Contains(Convert.ToInt32(instruction.Operand.ToString())))
                        {
                            indexList.Add(i);
                            if (removeIfFound)
                                operands.Remove(Convert.ToInt32(instruction.Operand.ToString()));
                        }
                    }
                    i++;
                }
                if (indexList.Count == operand.Length || removeIfFound == false)
                {
                    obfuscatedTarget.Indices = indexList;
                    obfuscatedTargets.Add(obfuscatedTarget);
                }
                operands = operand.ToList();
            }
            else
            {
                foreach (var method in type.Methods)
                {
                    if (method.Body != null)
                    {
                        List<int> indexList = new List<int>();
                        var obfuscatedTarget = new ObfuscatedTarget()
                        {
                            Type = type,
                            Method = method
                        };
                        int i = 0;
                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (instruction.Operand != null)
                            {
                                if (operands.Contains(Convert.ToInt32(instruction.Operand.ToString())))
                                {
                                    indexList.Add(i);
                                    if (removeIfFound)
                                        operands.Remove(Convert.ToInt32(instruction.Operand.ToString()));
                                }
                            }
                            i++;
                        }
                        if (indexList.Count == operand.Length || removeIfFound == false)
                        {
                            obfuscatedTarget.Indices = indexList;
                            obfuscatedTargets.Add(obfuscatedTarget);
                        }
                        operands = operand.ToList();
                    }
                }
            }

            List<Target> targets = new List<Target>();
            foreach (var obfuscatedTarget in obfuscatedTargets)
            {
                Target t = new Target()
                {
                    Namespace = obfuscatedTarget.Type.Namespace,
                    Class = obfuscatedTarget.Type.Name,
                    Method = obfuscatedTarget.Method.Name,
                    NestedClasses = obfuscatedTarget.NestedTypes.ToArray()
                };
                if (obfuscatedTarget.Indices.Count == 1)
                {
                    t.Index = obfuscatedTarget.Indices[0];
                }
                else if (obfuscatedTarget.Indices.Count > 1)
                {
                    t.Indices = obfuscatedTarget.Indices.ToArray();
                }

                targets.Add(t);
            }
            return targets.ToArray();
        }

        /// <summary>
        /// Find methods that contain a certain OpCode[] signature
        /// </summary>
        /// <returns></returns>
        public HashSet<MethodDef> FindMethodsByOpCodeSignature(OpCode[] signature)
        {
            HashSet<MethodDef> found = new HashSet<MethodDef>();

            foreach (TypeDef td in _module.Types)
            {
                foreach (MethodDef md in td.Methods)
                {
                    if (md.HasBody)
                    {
                        if (md.Body.HasInstructions)
                        {
                            OpCode[] ops = md.Body.Instructions.ToArray().GetOpCodes();
                            if (ops.IndexOf<OpCode>(signature).Count() > 0)
                            {
                                found.Add(md);
                            }
                        }
                    }
                }
            }

            return found;
        }

        public  Target[] FindInstructionsByOpcode(Target target, OpCode[] opcode, bool removeIfFound = false)
        {
            List<ObfuscatedTarget> obfuscatedTargets = new List<ObfuscatedTarget>();
            List<string> operands = opcode.Select(o => o.Name).ToList();
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef m = null;
            if (target.Method != null)
                m = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            if (m != null)
            {
                List<int> indexList = new List<int>();
                var obfuscatedTarget = new ObfuscatedTarget()
                {
                    Type = type,
                    Method = m
                };
                int i = 0;
                foreach (var instruction in m.Body.Instructions)
                {
                    if (operands.Contains(instruction.OpCode.Name))
                    {
                        indexList.Add(i);
                        if (removeIfFound)
                            operands.Remove(instruction.OpCode.Name);
                    }
                    i++;
                }
                if (indexList.Count == opcode.Length || removeIfFound == false)
                {
                    obfuscatedTarget.Indices = indexList;
                    obfuscatedTargets.Add(obfuscatedTarget);
                }
            }
            else
            {
                foreach (var method in type.Methods)
                {
                    if (method.Body != null)
                    {
                        List<int> indexList = new List<int>();
                        var obfuscatedTarget = new ObfuscatedTarget()
                        {
                            Type = type,
                            Method = method
                        };
                        int i = 0;
                        foreach (var instruction in method.Body.Instructions)
                        {
                            if (operands.Contains(instruction.OpCode.Name))
                            {
                                indexList.Add(i);
                                if (removeIfFound)
                                    operands.Remove(instruction.OpCode.Name);
                            }
                            i++;
                        }
                        if (indexList.Count == opcode.Length || removeIfFound == false)
                        {
                            obfuscatedTarget.Indices = indexList;
                            obfuscatedTargets.Add(obfuscatedTarget);
                        }
                        operands = opcode.Select(o => o.Name).ToList();
                    }
                }
            }

            List<Target> targets = new List<Target>();
            foreach (var obfuscatedTarget in obfuscatedTargets)
            {
                Target t = new Target()
                {
                    Namespace = obfuscatedTarget.Type.Namespace,
                    Class = obfuscatedTarget.Type.Name,
                    Method = obfuscatedTarget.Method.Name,
                    NestedClasses = obfuscatedTarget.NestedTypes.ToArray()
                };
                if (obfuscatedTarget.Indices.Count == 1)
                {
                    t.Index = obfuscatedTarget.Indices[0];
                }
                else if (obfuscatedTarget.Indices.Count > 1)
                {
                    t.Indices = obfuscatedTarget.Indices.ToArray();
                }

                targets.Add(t);
            }
            return targets.ToArray();
        }

        public  MemberRef BuildMemberRef(string ns, string cs, string name, Patcher.MemberRefType type)
        {
            TypeRef consoleRef = new TypeRefUser(_module, ns, cs, _module.CorLibTypes.AssemblyRef);
            if (type == Patcher.MemberRefType.Static)
            {
                return new MemberRefUser(_module, name,
                    MethodSig.CreateStatic(_module.CorLibTypes.Void, _module.CorLibTypes.String),
                    consoleRef);
            }
            else
            {
                return new MemberRefUser(_module, name,
                   MethodSig.CreateInstance(_module.CorLibTypes.Void, _module.CorLibTypes.String),
                   consoleRef);
            }
        }

        public  void ReplaceInstruction(Target target)
        {
            string[] nestedClasses = { };
            if (target.NestedClasses != null)
            {
                nestedClasses = target.NestedClasses;
            }
            else if (target.NestedClass != null)
            {
                nestedClasses = new[] { target.NestedClass };
            }
            var type = FindType(target.Namespace + "." + target.Class, nestedClasses);
            var method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Index != -1 && target.Instruction != null)
            {
                instructions[target.Index] = target.Instruction;
            }
            else if (target.Indices != null && target.Instructions != null)
            {
                foreach (var index in target.Indices)
                {
                    instructions[index] = target.Instructions[index];
                }
            }
            else
            {
                throw new Exception("Target object built wrong");
            }
        }

        public  void RemoveInstruction(Target target)
        {
            string[] nestedClasses = { };
            if (target.NestedClasses != null)
            {
                nestedClasses = target.NestedClasses;
            }
            else if (target.NestedClass != null)
            {
                nestedClasses = new[] { target.NestedClass };
            }
            var type = FindType(target.Namespace + "." + target.Class, nestedClasses);
            var method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Index != -1 && target.Indices == null)
            {
                instructions.RemoveAt(target.Index);
            }
            else if (target.Index == -1 && target.Indices != null)
            {
                foreach (var index in target.Indices.OrderByDescending(v => v))
                {
                    instructions.RemoveAt(index);
                }
            }
            else
            {
                throw new Exception("Target object built wrong");
            }
        }

        public  Instruction[] GetInstructions(Target target)
        {
            var type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            return (Instruction[])method.Body.Instructions;
        }

        public  void PatchOperand(Target target, string operand)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Indices == null && target.Index != -1)
            {
                instructions[target.Index].Operand = operand;
            }
            else if (target.Indices != null && target.Index == -1)
            {
                foreach (var index in target.Indices)
                {
                    instructions[index].Operand = operand;
                }
            }
            else
            {
                throw new Exception("Operand error");
            }
        }

        public  void PatchOperand(Target target, int operand)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Indices == null && target.Index != -1)
            {
                instructions[target.Index].Operand = operand;
            }
            else if (target.Indices != null && target.Index == -1)
            {
                foreach (var index in target.Indices)
                {
                    instructions[index].Operand = operand;
                }
            }
            else
            {
                throw new Exception("Operand error");
            }
        }

        public void PatchOperand(Target target, string[] operand)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Indices != null && target.Index == -1)
            {
                foreach (var index in target.Indices)
                {
                    instructions[index].Operand = operand[index];
                }
            }
            else
            {
                throw new Exception("Operand error");
            }
        }

        public  void PatchOperand(Target target, int[] operand)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            if (target.Indices != null && target.Index == -1)
            {
                foreach (var index in target.Indices)
                {
                    instructions[index].Operand = operand[index];
                }
            }
            else
            {
                throw new Exception("Operand error");
            }
        }

        public  string GetOperand(Target target)
        {
            TypeDef type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            return method.Body.Instructions[target.Index].Operand.ToString();
        }

        public  int FindInstruction(Target target, Instruction instruction, int occurence)
        {
            occurence--; // Fix the occurence, e.g. second occurence must be 1 but hoomans like to write like they speak so why don't assist them?
            var type = FindType(target.Namespace + "." + target.Class, target.NestedClasses);
            MethodDef method = FindMethod(type, target.Method, target.Parameters, target.ReturnType);
            var instructions = method.Body.Instructions;
            int index = 0;
            int occurenceCounter = 0;
            foreach (var i in instructions)
            {
                if (i.Operand == null && instruction.Operand == null)
                {
                    if (i.OpCode.Name == instruction.OpCode.Name && occurenceCounter < occurence)
                    {
                        occurenceCounter++;
                    }
                    else if (i.OpCode.Name == instruction.OpCode.Name && occurenceCounter == occurence)
                    {
                        return index;
                    }
                }
                else if (i.OpCode.Name == instruction.OpCode.Name && i.Operand.ToString() == instruction.Operand.ToString() &&
                         occurenceCounter < occurence)
                {
                    occurenceCounter++;
                }
                else if (i.OpCode.Name == instruction.OpCode.Name && i.Operand.ToString() == instruction.Operand.ToString() &&
                         occurenceCounter == occurence)
                {
                    return index;
                }
                index++;
            }
            return -1;
        }
    }
}
