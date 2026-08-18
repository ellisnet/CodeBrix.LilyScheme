// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
// it under the terms of the GNU Lesser General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Runtime;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyScheme.Tests;

/// <summary>
/// Guile's single-inheritance record model — boot-9.scm's records section — which the
/// exception-object machinery stands on: <c>#:parent</c> lays out the parent's fields
/// first, <c>record-type-fields</c> answers the complete layout, a predicate accepts
/// subtype instances, only an <c>#:extensible? #t</c> type may be a parent, an
/// <c>(immutable name)</c> field spec refuses a modifier, and a record IS a struct to
/// <c>struct-vtable</c> and <c>struct-ref</c>.
/// </summary>
public class RecordInheritanceTests
{
    private static string Value(string source)
    {
        string result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = new Interpreter();
            SchemeBootstrap.LoadCore(interpreter);
            foreach (object form in SchemeReader.ReadAll(source, "<test>"))
            {
                result = Printer.Write(
                    interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule));
            }
        });

        return result;
    }

    [Fact]
    public void a_subtype_lays_out_the_parents_fields_first()
    {
        //Arrange / Act
        string result = Value(
            "(let* ((base (make-record-type 'base '(a) #:extensible? #t))"
            + "       (child (make-record-type 'child '(b) #:parent base))"
            + "       (make (record-constructor child)))"
            + " (list (record-type-fields child)"
            + "       ((record-accessor child 'a) (make 1 2))"
            + "       ((record-accessor child 'b) (make 1 2))))");

        //Assert
        result.Should().Be("((a b) 1 2)");
    }

    [Fact]
    public void a_parent_predicate_accepts_a_subtype_instance_but_not_the_reverse()
    {
        //Arrange / Act
        string result = Value(
            "(let* ((base (make-record-type 'base '() #:extensible? #t))"
            + "       (child (make-record-type 'child '() #:parent base)))"
            + " (list ((record-predicate base) ((record-constructor child)))"
            + "       ((record-predicate child) ((record-constructor base)))"
            + "       (record-type-has-parent? child base)"
            + "       (record-type-has-parent? base child)))");

        //Assert
        result.Should().Be("(#t #f #t #f)");
    }

    [Fact]
    public void a_final_type_refuses_to_be_a_parent()
    {
        //Arrange / Act
        // Without #:extensible? #t a type is final — boot-9's "parent type is final".
        string result = Value(
            "(catch #t"
            + " (lambda () (make-record-type 'child '()"
            + "   #:parent (make-record-type 'base '())))"
            + " (lambda (key . args) key))");

        //Assert
        result.Should().Be("misc-error");
    }

    [Fact]
    public void an_immutable_field_spec_refuses_a_modifier_and_a_mutable_one_works()
    {
        //Arrange / Act
        string result = Value(
            "(let ((type (make-record-type 'spec '((immutable fixed) (mutable open)))))"
            + " (list (record-type-fields type)"
            + "       (catch #t (lambda () (record-modifier type 'fixed) 'allowed)"
            + "                 (lambda (key . args) key))"
            + "       (let ((instance ((record-constructor type) 1 2)))"
            + "         ((record-modifier type 'open) instance 9)"
            + "         ((record-accessor type 'open) instance))))");

        //Assert
        result.Should().Be("((fixed open) misc-error 9)");
    }

    [Fact]
    public void record_type_name_answers_the_symbol()
    {
        //Arrange / Act
        string result = Value("(record-type-name (make-record-type 'named '(x)))");

        //Assert
        result.Should().Be("named");
    }

    [Fact]
    public void a_record_is_a_struct_and_struct_ref_counts_fields_from_zero()
    {
        //Arrange / Act
        string result = Value(
            "(let* ((type (make-record-type 'point '(x y)))"
            + "       (instance ((record-constructor type) 3 4)))"
            + " (list (struct? instance)"
            + "       (eq? (struct-vtable instance) type)"
            + "       (struct-ref instance 0)"
            + "       (struct-ref instance 1)))");

        //Assert
        result.Should().Be("(#t #t 3 4)");
    }

    [Fact]
    public void a_record_prints_with_its_type_name_and_fields()
    {
        //Arrange / Act
        string result = Value(
            "(object->string ((record-constructor (make-record-type 'point '(x y))) 1 \"s\"))");

        //Assert
        result.Should().Be("\"#<point x: 1 y: \\\"s\\\">\"");
    }

    [Fact]
    public void srfi_9_records_still_work_flat()
    {
        //Arrange / Act
        string result = Value(
            "(define-record-type <pare> (kons x y) pare? (x kar set-kar!) (y kdr))"
            + "(let ((p (kons 1 2))) (set-kar! p 3) (list (pare? p) (kar p) (kdr p)))");

        //Assert
        result.Should().Be("(#t 3 2)");
    }
}
