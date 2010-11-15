//
// Import Checker; simple program to show what 1 or more dlls import
//
// Authors:
//  Carlo Kok <ck@remobjects.com>
//
// Copyright (C) 2010 RemObjects Software
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;

namespace ImportChecker
{
    class Worker
    {
        private importchecker output = new importchecker();
        private Dictionary<TypeReference, ImportedType> typeCache = new Dictionary<TypeReference, ImportedType>();
        private Dictionary<MemberReference, ImportedMember> memberCache = new Dictionary<MemberReference, ImportedMember>();

        public importchecker Output { get { return output; } }

        public Worker()
        {
        }

        internal void Work(Mono.Cecil.ModuleDefinition md)
        {
            foreach (TypeDefinition def in md.Types)
            {
                if (def.BaseType != null)
                    ImportType(def.BaseType);
                foreach (var gp in def.GenericParameters)
                    foreach (var ct in gp.Constraints)
                        ImportType(ct);
                foreach (var el in def.Interfaces)
                    ImplementType(el);

                foreach (var el in def.CustomAttributes)
                    ImportMember(el.Constructor);
                foreach (var el in def.Events)
                    ImportType(el.EventType);
                foreach (var el in def.Properties)
                {
                    ImportType(el.PropertyType);
                    if (el.HasParameters)
                        foreach (var par in el.Parameters)
                            ImportType(par.ParameterType);
                }
                foreach (var el in def.Fields)
                    ImportType(el.FieldType);
                foreach (var el in def.Methods)
                {
                    ImportType(el.ReturnType);
                    foreach (var gp in el.GenericParameters)
                        foreach (var ct in gp.Constraints)
                            ImportType(ct);
                    foreach (var par in el.Parameters)
                        ImportType(par.ParameterType);
                    foreach (var ov in el.Overrides)
                        ImplementMember(ov);
                    if (el.IsVirtual && !el.IsNewSlot)
                        ImplementMember(el);
                    if (el.HasBody) {
                        if (el.Body.HasExceptionHandlers)
                        foreach (var ex in el.Body.ExceptionHandlers)
                            ImportType(ex.CatchType);
                        foreach (var inst in el.Body.Instructions)
                        {
                            if (inst.Operand is TypeReference)
                                ImportType((TypeReference)inst.Operand);
                            else if (inst.Operand is MemberReference)
                                ImportMember((MemberReference)inst.Operand);
                        }
                    }
                }
            }
        }

        private void ImplementMember(MethodReference mref)
        {
            if (mref is MethodDefinition) return;

            ImportedType it = ImportType(mref.DeclaringType);
            if (it == null) return; // nothing to do here
            if (it.implemented == null) it.implemented = new List<ImplementedMember>();
            it.implemented.Add(new ImplementedMember
            {
                name = mref.Name,
                signature = mref.FullName
            });
        }

        private void ImplementType(TypeReference tref)
        {
            if (tref is TypeDefinition) return;
            if (tref is GenericInstanceType)
            {
                GenericInstanceType gt = (GenericInstanceType)tref;
                for (int i = 0; i < gt.GenericArguments.Count; i++)
                    ImportType(gt.GenericArguments[i]);
                if (gt.ElementType is TypeDefinition) return;
                   
            }
            ImportedType it = ImportType(tref);
            if (it != null)
            {
                if (it.implementedByType) return; // already done
                it.implementedByType = true;
                it.implementedByTypeSpecified = true;
                it.implemented = new List<ImplementedMember>();
                TypeDefinition type = tref.Resolve();
                foreach (var el in type.Methods) // at this point we have to presume the current class implements them all
                {
                    ImplementedMember im = new ImplementedMember();
                    im.name = el.Name;
                    im.signature = el.FullName;
                    it.implemented.Add(im);
                }
            }
        }

        private ImportedMember ImportMember(MemberReference mref)
        {
            if (mref == null) return null;
            if (mref is GenericInstanceMethod) {
                GenericInstanceMethod gm = (GenericInstanceMethod)mref;
                for (int i = 0; i < gm.GenericArguments.Count; i++)
                    ImportType(gm.GenericArguments[i]);
                mref = gm.ElementMethod;
            }
            if (mref is MethodDefinition) return null;
            if (memberCache.ContainsKey(mref)) return memberCache[mref];
            ImportedType it = ImportType(mref.DeclaringType);
            if (it == null) return null;
            
            ImportedMember mb = new ImportedMember();
            it.members.Add(mb);
            mb.name = mref.Name;
            mb.signature = mref.FullName;

            mb.kindSpecified = true;
            if (mref is FieldReference)
                mb.kind = ElementType.field;
            else
                mb.kind = ElementType.method;
            memberCache.Add(mref, mb);
            return mb;
        }

        private ImportedType ImportType(TypeReference tref)
        {
            if (tref == null) return null;
            switch (tref.MetadataType)
            {
                case MetadataType.Pointer:
                case MetadataType.Pinned:
                case MetadataType.Array:
                    ImportType(tref.GetElementType());
                    return null;
                    case MetadataType.OptionalModifier:
                case MetadataType.RequiredModifier:

                case MetadataType.ByReference:
                    return ImportType(tref.GetElementType());
                case MetadataType.Boolean:
                case MetadataType.Byte:
                case MetadataType.Char:
                case MetadataType.Double:
                case MetadataType.FunctionPointer:
                case MetadataType.GenericInstance:
                case MetadataType.Int16:
                case MetadataType.Int32:
                case MetadataType.Int64:
                case MetadataType.IntPtr:
                case MetadataType.MVar:
                case MetadataType.Object:
                case MetadataType.SByte:
                case MetadataType.Single:
                case MetadataType.String:
                case MetadataType.UInt16:
                case MetadataType.UInt32:
                case MetadataType.UInt64:
                case MetadataType.UIntPtr:
                case MetadataType.ValueType:
                case MetadataType.Var:
                case MetadataType.Void:
                case MetadataType.TypedByReference:
                    return null;
            }
            if (tref is GenericInstanceType)
            {
                GenericInstanceType gt = (GenericInstanceType)tref;
                for (int i = 0; i < gt.GenericArguments.Count; i++)
                    ImportType(gt.GenericArguments[i]);
                if (gt.ElementType is TypeDefinition) return null;
                tref = gt.ElementType;
            }
            if (tref is TypeDefinition) return null;
            if (typeCache.ContainsKey(tref)) return typeCache[tref];
            if (Program.IsFiltered(tref)) return null;
            var mod = output.library.Where(a => a.name == tref.Scope.Name).FirstOrDefault();
            if (mod == null)
            {
                mod = new ImportedLibrary();
                mod.name = tref.Scope.Name;
                mod.fullname = tref.Module.AssemblyReferences.Where(a => a.Name == tref.Scope.Name).FirstOrDefault().FullName;
                output.library.Add(mod);
            }
            var it = new ImportedType();
            it.name = tref.ToString();
            mod.type.Add(it);
            typeCache.Add(tref, it);
            return it;
        }
    }
}
