// GenJs.cs - JavaScript code generator
//
// Copyright (C) 2011-2019  Piotr Fusik
//
// This file is part of CiTo, see https://github.com/pfusik/cito
//
// CiTo is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// CiTo is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with CiTo.  If not, see http://www.gnu.org/licenses/

using System;
using System.Collections.Generic;
using System.Linq;

namespace Foxoft.Ci
{

enum GenJsMethod
{
	CopyArray,
	UTF8GetString,
	Count
}

public class GenJs : GenBase
{
	// TODO: Namespace
	readonly string[][] Library = new string[(int) GenJsMethod.Count][];

	protected override void WriteName(CiSymbol symbol)
	{
		if (symbol is CiConst konst) {
			if (konst.InMethod != null) {
				WriteUppercaseWithUnderscores(konst.InMethod.Name);
				Write('_');
			}
			WriteUppercaseWithUnderscores(symbol.Name);
		}
		else if (symbol is CiMember) {
			if (symbol == CiSystem.CollectionCount)
				Write("length");
			else
				WriteCamelCase(symbol.Name);
		}
		else
			Write(symbol.Name);
	}

	protected override void WriteTypeAndName(CiNamedValue value)
	{
		WriteName(value);
	}

	public override CiExpr Visit(CiCollection expr, CiPriority parent)
	{
		Write("[ ");
		WriteCoercedLiterals(null, expr.Items);
		Write(" ]");
		return expr;
	}

	protected override void WriteListStorageInit(CiListType list)
	{
		Write(" = new Array()");
	}

	protected override void WriteSortedDictionaryStorageInit(CiSortedDictionaryType dict)
	{
		Write(" = new Object()");
	}

	protected override void WriteVar(CiNamedValue def)
	{
		Write("let ");
		base.WriteVar(def);
	}

	public override CiExpr Visit(CiInterpolatedString expr, CiPriority parent)
	{
		Write('`');
		foreach (CiInterpolatedPart part in expr.Parts) {
			string s = part.Prefix;
			for (int i = 0; i < s.Length; i++) {
				char c = s[i];
				if (c == '`'
				 || (c == '$' && i + 1 < s.Length && s[i + 1] == '{'))
					Write('\\');
				WriteEscapedChar(c);
			}
			if (part.Argument != null) {
				Write("${");
				if (part.Width != 0 || part.Format != ' ') {
					part.Argument.Accept(this, CiPriority.Primary);
					if (part.Argument.Type is CiNumericType) {
						switch (part.Format) {
						case 'E':
							Write(".toExponential(");
							if (part.Precision >= 0)
								Write(part.Precision);
							Write(").toUpperCase()");
							break;
						case 'e':
							Write(".toExponential(");
							if (part.Precision >= 0)
								Write(part.Precision);
							Write(')');
							break;
						case 'F':
						case 'f':
							Write(".toFixed(");
							if (part.Precision >= 0)
								Write(part.Precision);
							Write(')');
							break;
						case 'X':
							Write(".toString(16).toUpperCase()");
							break;
						case 'x':
							Write(".toString(16)");
							break;
						default:
							Write(".toString()");
							break;
						}
						if (part.Precision >= 0 && "DdXx".IndexOf(part.Format) >= 0) {
							Write(".padStart(");
							Write(part.Precision);
							Write(", \"0\")");
						}
					}
					if (part.Width > 0) {
						Write(".padStart(");
						Write(part.Width);
						Write(')');
					}
					else if (part.Width < 0) {
						Write(".padEnd(");
						Write(-part.Width);
						Write(')');
					}
				}
				else
					part.Argument.Accept(this, CiPriority.Statement);
				Write('}');
			}
		}
		Write('`');
		return expr;
	}

	void WriteMember(CiMember member)
	{
		Write(member.IsStatic() ? this.CurrentMethod.Parent.Name : "this");
		Write('.');
		WriteName(member);
	}

	public override CiExpr Visit(CiSymbolReference expr, CiPriority parent)
	{
		if (expr.Left == null && expr.Symbol is CiMember member)
			WriteMember(member);
		else
			base.Visit(expr, parent);
		return expr;
	}

	protected override void WriteNewArray(CiType elementType, CiExpr lengthExpr, CiPriority parent)
	{
		if (!(elementType is CiNumericType)) {
			Write("new Array(");
			lengthExpr.Accept(this, CiPriority.Statement);
			Write(')');
			return;
		}

		string name;
		int shift;
		if (elementType == CiSystem.IntType) {
			name = "Int32";
			shift = 2;
		}
		else if (elementType == CiSystem.DoubleType) {
			name = "Float64";
			shift = 3;
		}
		else if (elementType == CiSystem.FloatType) {
			name = "Float32";
			shift = 2;
		}
		else if (elementType == CiSystem.LongType) {
			// TODO: UInt32 if possible?
			name = "Float64"; // no 64-bit integers in JavaScript
			shift = 3;
		}
		else {
			CiRangeType range = (CiRangeType) elementType;
			if (range.Min < 0) {
				if (range.Min < short.MinValue || range.Max > short.MaxValue) {
					name = "Int32";
					shift = 2;
				}
				else if (range.Min < sbyte.MinValue || range.Max > sbyte.MaxValue) {
					name = "Int16";
					shift = 1;
				}
				else {
					name = "Int8";
					shift = 0;
				}
			}
			else if (range.Max > ushort.MaxValue) {
				name = "Int32";
				shift = 2;
			}
			else if (range.Max > byte.MaxValue) {
				name = "UInt16";
				shift = 1;
			}
			else {
				name = "Uint8";
				shift = 0;
			}
		}

		Write("new ");
		Write(name);
		Write("Array(new ArrayBuffer(");
		if (shift == 0)
			lengthExpr.Accept(this, CiPriority.Statement);
		else if (lengthExpr is CiLiteral literalLength)
			Write(((long) literalLength.Value) << shift);
		else {
			lengthExpr.Accept(this, CiPriority.Shift);
			Write(" << ");
			Write(shift);
		}
		Write("))");
	}

	bool HasInitCode(CiNamedValue def)
	{
		return def.Type is CiArrayStorageType array
			&& (array.ElementType is CiClass || array.ElementType is CiArrayStorageType);
	}

	protected override void WriteInitCode(CiNamedValue def)
	{
		if (!HasInitCode(def))
			return;
		CiArrayStorageType array = (CiArrayStorageType) def.Type;
		int nesting = 0;
		while (array.ElementType is CiArrayStorageType innerArray) {
			OpenLoop("let", nesting++, array.Length);
			WriteArrayElement(def, nesting);
			Write(" = ");
			WriteNewArray(innerArray.ElementType, innerArray.LengthExpr, CiPriority.Statement);
			WriteLine(';');
			array = innerArray;
		}
		if (array.ElementType is CiClass klass) {
			OpenLoop("let", nesting++, array.Length);
			WriteArrayElement(def, nesting);
			Write(" = ");
			WriteNew(klass, CiPriority.Statement);
			WriteLine(';');
		}
		while (--nesting >= 0)
			CloseBlock();
	}

	protected override void WriteResource(string name, int length)
	{
		if (length >= 0) // reference as opposed to definition
			Write("Ci.");
		foreach (char c in name)
			Write(CiLexer.IsLetterOrDigit(c) ? c : '_');
	}

	protected override void WriteStringLength(CiExpr expr)
	{
		expr.Accept(this, CiPriority.Primary);
		Write(".length");
	}

	protected override void WriteCharAt(CiBinaryExpr expr)
	{
		expr.Left.Accept(this, CiPriority.Primary);
		Write(".charCodeAt(");
		expr.Right.Accept(this, CiPriority.Statement);
		Write(')');
	}

	void AddLibrary(GenJsMethod id, params string[] method)
	{
		if (this.Library[(int) id] == null)
			this.Library[(int) id] = method;
	}

	protected override void WriteCall(CiExpr obj, CiMethod method, CiExpr[] args, CiPriority parent)
	{
		if (method == CiSystem.StringSubstring) {
			obj.Accept(this, CiPriority.Primary);
			Write(".substring(");
			args[0].Accept(this, CiPriority.Statement);
			if (args.Length == 2) {
				Write(", ");
				args[0].Accept(this, CiPriority.Add); // TODO: side effect
				Write(" + ");
				args[1].Accept(this, CiPriority.Add);
			}
			Write(')');
		}
		else if (obj.Type is CiArrayType && method.Name == "CopyTo") {
			AddLibrary(GenJsMethod.CopyArray,
				"copyArray : function(sa, soffset, da, doffset, length)",
				"for (let i = 0; i < length; i++)",
				"\tda[doffset + i] = sa[soffset + i];");
			Write("Ci.copyArray(");
			obj.Accept(this, CiPriority.Statement);
			Write(", ");
			WriteArgs(method, args);
			Write(')');
		}
		else if (obj.Type is CiListType && method.Name == "Add") {
			obj.Accept(this, CiPriority.Primary);
			Write(".push");
			WriteArgsInParentheses(method, args);
		}
		else if (method == CiSystem.CollectionClear) {
			obj.Accept(this, CiPriority.Primary);
			Write(".length = 0");
		}
		else if (obj.Type is CiListType && method.Name == "Insert") {
			obj.Accept(this, CiPriority.Primary);
			Write(".splice(");
			args[0].Accept(this, CiPriority.Statement);
			Write(", 0, ");
			args[1].Accept(this, CiPriority.Statement);
			Write(')');
		}
		else if (method == CiSystem.ListRemoveAt) {
			obj.Accept(this, CiPriority.Primary);
			Write(".splice(");
			args[0].Accept(this, CiPriority.Statement);
			Write(", 1)");
		}
		else if (method == CiSystem.ListRemoveRange) {
			obj.Accept(this, CiPriority.Primary);
			Write(".splice(");
			args[0].Accept(this, CiPriority.Statement);
			Write(", ");
			args[1].Accept(this, CiPriority.Statement);
			Write(')');
		}
		else if (method == CiSystem.ConsoleWrite || method == CiSystem.ConsoleWriteLine) {
			// XXX: Console.Write same as Console.WriteLine
			if (obj is CiSymbolReference symbol && symbol.Symbol == CiSystem.ConsoleError)
				Write("console.error");
			else
				Write("console.log");
			if (args.Length == 0)
				Write("(\"\")");
			else
				WriteArgsInParentheses(method, args);
		}
		else if (method == CiSystem.UTF8GetString) {
			AddLibrary(GenJsMethod.UTF8GetString,
				"utf8GetString : function(a, i, length)",
				"length += i;",
				"let s = \"\";",
				"while (i < length) {",
				"\tlet c = a[i];",
				"\tif (c < 0x80)",
				"\t\ti++;",
				"\telse if ((c & 0xe0) == 0xc0) {",
				"\t\tc = (c & 0x1f) << 6 | (a[i + 1] & 0x3f);",
				"\t\ti += 2;",
				"\t}",
				"\telse if ((c & 0xf0) == 0xe0) {",
				"\t\tc = (c & 0xf) << 12 | (a[i + 1] & 0x3f) << 6 | (a[i + 2] & 0x3f);",
				"\t\ti += 3;",
				"\t}",
				"\ts += String.fromCharCode(c);",
				"}",
				"return s;");
			Write("Ci.utf8GetString");
			WriteArgsInParentheses(method, args);
		}
		else if (IsMathReference(obj) && method.Name == "FusedMultiplyAdd") {
			if (parent > CiPriority.Add)
				Write('(');
			args[0].Accept(this, CiPriority.Mul);
			Write(" * ");
			args[1].Accept(this, CiPriority.Mul);
			Write(" + ");
			args[2].Accept(this, CiPriority.Add);
			if (parent > CiPriority.Add)
				Write(')');
		}
		else {
			obj.Accept(this, CiPriority.Primary);
			Write('.');
			if (IsMathReference(obj) && method.Name == "Ceiling")
				Write("ceil");
			else if (IsMathReference(obj) && method.Name == "Truncate")
				Write("trunc");
			else if (method == CiSystem.StringContains)
				Write("includes");
			else
				WriteCamelCase(method.Name);
			WriteArgsInParentheses(method, args);
		}
	}

	protected override void WriteNearCall(CiMethod method, CiExpr[] args)
	{
		WriteMember(method);
		WriteArgsInParentheses(method, args);
	}

	public override CiExpr Visit(CiBinaryExpr expr, CiPriority parent)
	{
		if (expr.Op == CiToken.Slash && expr.Type is CiIntegerType) {
			if (parent > CiPriority.Or)
				Write('(');
			expr.Left.Accept(this, CiPriority.Mul);
			Write(" / ");
			expr.Right.Accept(this, CiPriority.Primary);
			Write(" | 0"); // FIXME: long: Math.trunc?
			if (parent > CiPriority.Or)
				Write(')');
			return expr;
		}
		return base.Visit(expr, parent);
	}

	public override void Visit(CiForeach statement)
	{
		Write("for (const ");
		Write(statement.Element.Name);
		Write(" of ");
		statement.Collection.Accept(this, CiPriority.Statement);
		Write(')');
		WriteChild(statement.Body);
	}

	public override void Visit(CiThrow statement)
	{
		Write("throw ");
		statement.Message.Accept(this, CiPriority.Statement);
		WriteLine(';');
	}

	void Write(CiEnum enu)
	{
		WriteLine();
		Write(enu.Documentation);
		Write("const ");
		Write(enu.Name);
		Write(" = ");
		OpenBlock();
		int i = 0;
		foreach (CiConst konst in enu) {
			if (i > 0)
				WriteLine(',');
			Write(konst.Documentation);
			WriteUppercaseWithUnderscores(konst.Name);
			Write(" : ");
			if (konst.Value != null)
				konst.Value.Accept(this, CiPriority.Statement);
			else
				Write(i);
			i++;
		}
		WriteLine();
		CloseBlock();
	}

	void Write(CiClass klass, CiMethod method)
	{
		if (method.CallType == CiCallType.Abstract)
			return;
		WriteLine();
		WriteDoc(method);
		Write(klass.Name);
		Write('.');
		if (method.CallType != CiCallType.Static)
			Write("prototype.");
		WriteCamelCase(method.Name);
		Write(" = function(");
		bool first = true;
		foreach (CiVar param in method.Parameters) {
			if (!first)
				Write(", ");
			first = false;
			Write(param.Name);
			if (param.Value != null) {
				Write(" = ");
				param.Value.Accept(this, CiPriority.Statement);
			}
		}
		Write(')');
		WriteBody(method);
	}

	void WriteConsts(CiClass klass, IEnumerable<CiConst> consts)
	{
		foreach (CiConst konst in consts) {
			if (konst.Visibility != CiVisibility.Private || konst.Type is CiArrayStorageType) {
				WriteLine();
				Write(konst.Documentation);
				Write(klass.Name);
				Write('.');
				base.WriteVar(konst);
				WriteLine(';');
			}
		}
	}

	void Write(CiClass klass)
	{
		WriteLine();
		Write(klass.Documentation);
		Write("function ");
		Write(klass.Name);
		WriteLine("()");
		OpenBlock();
		foreach (CiField field in klass.Fields) {
			if (field.Value != null || field.Type is CiClass || field.Type is CiArrayStorageType) {
				Write("this.");
				base.WriteVar(field);
				WriteLine(';');
				WriteInitCode(field);
			}
		}
		WriteConstructorBody(klass);
		CloseBlock();
		if (klass.BaseClassName != null) {
			Write(klass.Name);
			Write(".prototype = new ");
			Write(klass.BaseClassName);
			WriteLine("();");
		}
		WriteConsts(klass, klass.Consts);
		foreach (CiMethod method in klass.Methods)
			Write(klass, method);
		WriteConsts(klass, klass.ConstArrays);
	}

	void WriteLib(Dictionary<string, byte[]> resources)
	{
		WriteLine();
		Write("const Ci = ");
		OpenBlock();
		bool first = true;
		foreach (string[] method in this.Library) {
			if (method != null) {
				if (!first)
					WriteLine(',');
				first = false;
				WriteLine(method[0]);
				OpenBlock();
				for (int i = 1; i < method.Length; i++)
					WriteLine(method[i]);
				this.Indent--;
				Write('}');
			}
		}

		foreach (string name in resources.Keys.OrderBy(k => k)) {
			if (!first)
				WriteLine(',');
			first = false;
			WriteResource(name, -1);
			WriteLine(" : [");
			Write('\t');
			Write(resources[name]);
			Write(" ]");
		}
		WriteLine();
		CloseBlock();
	}

	public override void Write(CiProgram program)
	{
		CreateFile(this.OutputFile);
		WriteLine();
		WriteLine("\"use strict\";");
		WriteTopLevelNatives(program);
		foreach (CiEnum enu in program.OfType<CiEnum>())
			Write(enu);
		foreach (CiClass klass in program.OfType<CiClass>()) // TODO: topological sort of class hierarchy
			Write(klass);
		if (program.Resources.Count > 0 || this.Library.Any(l => l != null))
			WriteLib(program.Resources);
		CloseFile();
	}
}

}

