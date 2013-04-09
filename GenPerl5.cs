// GenPerl5.cs - Perl 5 code generator
//
// Copyright (C) 2013  Piotr Fusik
//
// This file is part of CiTo, see http://cito.sourceforge.net
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

namespace Foxoft.Ci
{

public class GenPerl5 : SourceGenerator, ICiSymbolVisitor
{
	string Package;
	CiMethod CurrentMethod;

	public GenPerl5(string package)
	{
		this.Package = package == null ? string.Empty : package + "::";
	}

	protected override void WriteBanner()
	{
		WriteLine("# Generated automatically with \"cito\". Do not edit.");
	}

	void WritePackage(CiSymbol symbol)
	{
		Write("package ");
		Write(this.Package);
		Write(symbol.Name);
		WriteLine(";");
	}

	void ICiSymbolVisitor.Visit(CiEnum enu)
	{
		WritePackage(enu);
		for (int i = 0; i < enu.Values.Length; i++) {
			Write("sub ");
			WriteUppercaseWithUnderscores(enu.Values[i].Name);
			Write("() { ");
			Write(i);
			WriteLine(" }");
		}
		WriteLine();
	}

	protected override void WriteConst(object value)
	{
		if (value is bool)
			Write((bool) value ? '1' : '0');
		else if (value is byte)
			Write((byte) value);
		else if (value is int)
			Write((int) value);
		else if (value is string) {
			Write('\'');
			foreach (char c in (string) value) {
				switch (c) {
				case '\t': Write("\\t"); break;
				case '\r': Write("\\r"); break;
				case '\n': Write("\\n"); break;
				case '\\': Write("\\\\"); break;
				case '\'': Write("\\\'"); break;
				default: Write(c); break;
				}
			}
			Write('\'');
		}
		else if (value is CiEnumValue) {
			CiEnumValue ev = (CiEnumValue) value;
			Write(ev.Type.Name);
			Write("::");
			WriteUppercaseWithUnderscores(ev.Name);
		}
		else if (value is Array) {
			Write("( ");
			WriteContent((Array) value);
			Write(" )");
		}
		else if (value == null)
			Write("undef");
		else
			throw new ArgumentException(value.ToString());
	}

	void WriteName(CiVar v)
	{
		if (v == this.CurrentMethod.This)
			Write("self");
		else
			Write(v.Name);
	}

	protected override void WriteName(CiConst konst)
	{
		WriteUppercaseWithUnderscores(konst.GlobalName ?? konst.Name);
	}

	protected override void Write(CiVarAccess expr)
	{
		Write(expr.Var.Type is CiArrayStorageType ? '@' : '$');
		WriteName(expr.Var);
	}

	protected override void Write(CiFieldAccess expr)
	{
		WriteChild(1, expr.Obj);
		Write("->{");
		WriteLowercaseWithUnderscores(expr.Field.Name);
		Write('}');
	}

	protected override void Write(CiPropertyAccess expr)
	{
		if (expr.Property == CiLibrary.SByteProperty) {
			Write('(');
			WriteChild(9, expr.Obj);
			Write(" ^ 128) - 128");
		}
		else if (expr.Property == CiLibrary.LowByteProperty) {
			WriteChild(expr, expr.Obj);
			Write(" & 0xff");
		}
		else if (expr.Property == CiLibrary.StringLengthProperty) {
			Write("length(");
			Write(expr.Obj);
			Write(')');
		}
		else
			throw new ArgumentException(expr.Property.Name);
	}

	protected override void Write(CiArrayAccess expr)
	{
		CiVarAccess va = expr.Array as CiVarAccess;
		if (va != null) {
			Write('$');
			WriteName(va.Var);
			if (va.Type is CiArrayPtrType)
				Write("->");
		}
		else {
			CiConstAccess ca = expr.Array as CiConstAccess;
			if (ca != null) {
				Write('$');
				WriteName(ca.Const);
			}
			else
				WriteChild(expr, expr.Array);
		}
		Write('[');
		Write(expr.Index);
		Write(']');
	}

	void WriteSlice(CiExpr array, CiExpr index, CiExpr lenMinus1)
	{
		Write(array);
		Write('[');
		Write(index);
		Write(" .. ");
		WriteSum(index, lenMinus1);
		Write(']');
	}

	protected override void Write(CiMethodCall expr)
	{
		if (expr.Method == CiLibrary.MulDivMethod) {
			Write("int(");
			WriteMulDiv(3, expr);
		}
		else if (expr.Method == CiLibrary.CharAtMethod) {
			Write("ord(substr(");
			Write(expr.Obj);
			Write(", ");
			Write(expr.Arguments[0]);
			Write(", 1))");
		}
		else if (expr.Method == CiLibrary.SubstringMethod) {
			Write("substr(");
			Write(expr.Obj);
			Write(", ");
			Write(expr.Arguments[0]);
			Write(", ");
			Write(expr.Arguments[1]);
			Write(')');
		}
		else if (expr.Method == CiLibrary.ArrayCopyToMethod) {
			CiExpr lenMinus1 = new CiBinaryExpr { Left = expr.Arguments[3], Op = CiToken.Minus, Right = new CiConstExpr(1) };
			WriteSlice(expr.Arguments[1], expr.Arguments[2], lenMinus1);
			Write(" = ");
			WriteSlice(expr.Obj, expr.Arguments[0], lenMinus1);
		}
		else if (expr.Method == CiLibrary.ArrayToStringMethod) {
			Write("pack('U*', ");
			Write(expr.Obj);
			Write(')');
			// TODO Write(expr.Arguments[0]);
			// TODO Write(expr.Arguments[1]);
		}
		else if (expr.Method == CiLibrary.ArrayStorageClearMethod) {
			Write(expr.Obj);
			Write(" = (0) x ");
			Write(((CiArrayStorageType) expr.Obj.Type).Length);
		}
		else {
			if (expr.Method != null) {
				if (expr.Obj != null) {
					Write(expr.Obj);
					Write("->");
				}
				else {
					Write(this.Package);
					Write(expr.Method.Class.Name);
					Write("::");
				}
				WriteLowercaseWithUnderscores(expr.Method.Name);
			}
			else {
				// delegate call
				Write(expr.Obj);
				Write("->");
			}
			WriteArguments(expr);
		}
	}

	protected override void Write(CiBinaryExpr expr)
	{
		if (expr.Op == CiToken.Slash) {
			Write("int(");
			WriteChild(3, expr.Left);
			Write(" / ");
			WriteNonAssocChild(3, expr.Right);
			Write(')');
		}
		else
			base.Write(expr);
	}

	protected override void WriteNew(CiType type)
	{
		// TODO
	}

	protected override void Write(CiCoercion expr)
	{
		if (expr.ResultType is CiArrayPtrType) {
			Write('\\');
			WriteChild(expr, (CiExpr) expr.Inner); // TODO: Assign
		}
		else
			base.Write(expr);
	}

	protected override void WriteChild(ICiStatement stmt)
	{
		Write(' ');
		OpenBlock();
		CiBlock block = stmt as CiBlock;
		if (block != null)
			Write(block.Statements);
		else
			Write(stmt);
		CloseBlock();
	}

	public override void Visit(CiBreak stmt)
	{
		// TODO: switch
		WriteLine("last;");
	}

	public override void Visit(CiContinue stmt)
	{
		WriteLine("next;");
	}

	public override void Visit(CiIf stmt)
	{
		Write("if (");
		Write(stmt.Cond);
		Write(')');
		WriteChild(stmt.OnTrue);
		if (stmt.OnFalse != null) {
			Write("els");
			if (stmt.OnFalse is CiIf)
				Write(stmt.OnFalse);
			else {
				Write('e');
				WriteChild(stmt.OnFalse);
			}
		}
	}

	public override void Visit(CiVar stmt)
	{
		Write("my ");
		Write(stmt.Type is CiArrayStorageType ? '@' : '$');
		Write(stmt.Name);
		if (stmt.InitialValue != null) {
			Write(" = ");
			Write(stmt.InitialValue);
		}
	}

	public override void Visit(CiSwitch stmt)
	{
		bool tmpVar = stmt.Value.HasSideEffect;
		if (tmpVar) {
			OpenBlock();
			Write("my $CISWITCH = ");
			Write(stmt.Value);
			WriteLine(";");
		}
		for (int i = 0; i < stmt.Cases.Length; i++) {
			CiCase kase = stmt.Cases[i];
			if (kase.Value != null) {
				if (i > 0)
					Write("els");
				Write("if (");
				for (;;) {
					if (tmpVar)
						Write("$CISWITCH");
					else
						WriteChild(7, stmt.Value);
					Write(" == ");
					WriteConst(kase.Value);
					if (kase.Body.Length > 0 || i + 1 >= stmt.Cases.Length)
						break;
					Write(" || ");
					// TODO: "case 5: default:"
					// TODO: optimize ranges "case 1: case 2: case 3:"
					kase = stmt.Cases[++i];
				}
				Write(") ");
			}
			else
				Write("else "); // TODO: default that doesn't come last
			OpenBlock();
			int length = kase.Body.Length;
			if (length > 0 && kase.Body[length - 1] is CiBreak)
				length--;
			Write(kase.Body, length); // TODO: handle premature break and fallthrough with gotos
			CloseBlock();
		}
		if (tmpVar)
			CloseBlock();
	}

	public override void Visit(CiThrow stmt)
	{
		Write("die ");
		Write(stmt.Message);
		WriteLine(";");
	}

	void ICiSymbolVisitor.Visit(CiField field)
	{
	}

	void ICiSymbolVisitor.Visit(CiMethod method)
	{
		if (method.CallType == CiCallType.Abstract)
			return;
		this.CurrentMethod = method;
		Write("sub ");
		WriteLowercaseWithUnderscores(method.Name);
		Write('(');
		if (method.CallType != CiCallType.Static)
			Write('$');
		foreach (CiParam param in method.Signature.Params)
			Write('$');
		Write(") ");
		OpenBlock();
		Write("my (");
		bool first = true;
		if (method.CallType != CiCallType.Static) {
			Write("$self");
			first = false;
		}
		foreach (CiParam param in method.Signature.Params) {
			if (first)
				first = false;
			else
				Write(", ");
			Write('$');
			Write(param.Name);
		}
		WriteLine(") = @_;");
		Write(method.Body.Statements);
		CloseBlock();
		WriteLine();
		this.CurrentMethod = null;
	}

	void ICiSymbolVisitor.Visit(CiClass klass)
	{
		WritePackage(klass);
		if (klass.BaseClass != null) {
			Write("our @ISA = qw(");
			Write(this.Package);
			Write(klass.BaseClass.Name);
			WriteLine(");");
		}
		WriteLine();
		if (klass.Constructor != null) {
			this.CurrentMethod = klass.Constructor;
			Write("sub new($) ");
			OpenBlock();
			WriteLine("my $self = bless {}, shift;");
			Write(klass.Constructor.Body.Statements);
			WriteLine("return $self;"); // TODO: premature returns
			CloseBlock();
			WriteLine();
			this.CurrentMethod = null;
		}
		foreach (CiSymbol member in klass.Members)
			member.Accept(this);
		foreach (CiConst konst in klass.ConstArrays) {
			Write("our @");
			WriteUppercaseWithUnderscores(konst.GlobalName);
			Write(" = ");
			WriteConst(konst.Value);
			WriteLine(";");
		}
		foreach (CiBinaryResource resource in klass.BinaryResources) {
			Write("our @");
			WriteName(resource);
			Write(" = ");
			WriteConst(resource.Content);
			WriteLine(";");
		}
		WriteLine();
	}

	void ICiSymbolVisitor.Visit(CiDelegate del)
	{
	}

	public override void Write(CiProgram prog)
	{
		CreateFile(this.OutputFile);
		foreach (CiSymbol symbol in prog.Globals)
			symbol.Accept(this);
		WriteLine("1;");
		CloseFile();
	}
}

}
