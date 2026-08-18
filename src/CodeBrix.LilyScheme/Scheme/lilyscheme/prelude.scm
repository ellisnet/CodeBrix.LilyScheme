;;;; prelude.scm -- LilyScheme's replacement for the derived-syntax parts of boot-9
;;;;
;;;; Copyright (c) 2026 Jeremy Ellis and contributors
;;;;
;;;; CodeBrix.LilyScheme is free software: you can redistribute it and/or modify
;;;; it under the terms of the GNU Lesser General Public License as published by
;;;; the Free Software Foundation, either version 3 of the License, or
;;;; (at your option) any later version.
;;;;
;;;; THIS FILE IS NEW-IN-FAMILY. It is not derived from Guile source; it is written
;;;; against R7RS and the Guile reference manual, which describe the behaviour of
;;;; these forms without dictating an implementation.
;;;;
;;;; WHY THIS EXISTS
;;;;
;;;; Guile's ice-9/boot-9.scm defines the derived syntax below, but it also builds
;;;; Guile's entire module system from scratch on top of low-level vtable layouts,
;;;; weak tables, port types and the exception hierarchy -- and it opens by asserting
;;;; that (current-module) is #f, because it runs before a module system exists.
;;;; LilyScheme provides the module system from C# instead, so boot-9 cannot be
;;;; loaded verbatim. This file supplies the part LilyPond actually needs: the
;;;; derived special forms, expressed in syntax-rules on top of psyntax.
;;;;
;;;; Everything here is ordinary R7RS-style syntax. Nothing in it is Guile-specific,
;;;; which is exactly why it can be written fresh rather than vendored.

;;; ---------------------------------------------------------------------------
;;; Boolean connectives
;;;
;;; These must come first: everything below is written in terms of them. Note that
;;; psyntax does NOT provide and/or -- its core forms are quote, if, lambda, let,
;;; letrec, begin, set! and define, and everything else in Guile comes from boot-9.
;;; ---------------------------------------------------------------------------

(define-syntax and
  (syntax-rules ()
    ((_) #t)
    ((_ e) e)
    ((_ e1 e2 ...) (if e1 (and e2 ...) #f))))

(define-syntax or
  (syntax-rules ()
    ((_) #f)
    ((_ e) e)
    ((_ e1 e2 ...) (let ((first e1)) (if first first (or e2 ...))))))

;;; ---------------------------------------------------------------------------
;;; Conditionals
;;; ---------------------------------------------------------------------------

(define-syntax cond
  (syntax-rules (else =>)
    ((_ (else e ...)) (begin e ...))
    ((_ (test => proc) clause ...)
     (let ((tmp test)) (if tmp (proc tmp) (cond clause ...))))
    ((_ (test) clause ...)
     (let ((tmp test)) (if tmp tmp (cond clause ...))))
    ((_ (test e ...) clause ...)
     (if test (begin e ...) (cond clause ...)))
    ((_) *unspecified*)))

(define-syntax case
  (syntax-rules (else =>)
    ;; R7RS requires the key evaluated EXACTLY ONCE. The recursion below splices
    ;; the key expression into every clause test, which is only correct once the
    ;; key is an atom -- so a compound key expression is bound first and dispatch
    ;; happens on the resulting (pure) variable. This is R7RS 7.3's own pattern.
    ;; Without it, a side-effecting key such as ice-9/format.scm's
    ;; (case (char-upcase (next-char)) ...) is re-evaluated per clause.
    ((_ (key ...) clause ...)
     (let ((atom-key (key ...))) (case atom-key clause ...)))
    ((_ key (else => proc)) (proc key))
    ((_ key (else e ...)) (begin e ...))
    ((_ key ((datum ...) => proc) clause ...)
     (if (memv key '(datum ...)) (proc key) (case key clause ...)))
    ((_ key ((datum ...) e ...) clause ...)
     (if (memv key '(datum ...)) (begin e ...) (case key clause ...)))
    ((_ key) *unspecified*)))

(define-syntax when
  (syntax-rules ()
    ((_ test e ...) (if test (begin e ...) *unspecified*))))

(define-syntax unless
  (syntax-rules ()
    ((_ test e ...) (if test *unspecified* (begin e ...)))))

;;; ---------------------------------------------------------------------------
;;; Iteration
;;; ---------------------------------------------------------------------------

(define-syntax do
  (syntax-rules ()
    ((_ ((var init step ...) ...) (test expr ...) command ...)
     (letrec ((loop (lambda (var ...)
                      (if test
                          (begin *unspecified* expr ...)
                          (begin command ...
                                 (loop (do-step var step ...) ...))))))
       (loop init ...)))))

;; A do-binding may omit its step, in which case the variable keeps its value.
(define-syntax do-step
  (syntax-rules ()
    ((_ x) x)
    ((_ x y) y)))

;;; ---------------------------------------------------------------------------
;;; Multiple values
;;; ---------------------------------------------------------------------------

(define-syntax let-values
  (syntax-rules ()
    ((_ () body ...) (let () body ...))
    ((_ ((formals expr) rest ...) body ...)
     (call-with-values
       (lambda () expr)
       (lambda formals (let-values (rest ...) body ...))))))

(define-syntax let*-values
  (syntax-rules ()
    ((_ () body ...) (let () body ...))
    ((_ ((formals expr) rest ...) body ...)
     (call-with-values
       (lambda () expr)
       (lambda formals (let*-values (rest ...) body ...))))))

(define-syntax define-values
  (syntax-rules ()
    ((_ (name ...) expr)
     (begin
       (define name *unspecified*) ...
       ;; Build one setter per name and walk them alongside the produced values.
       ;; Writing (lambda (v ...) (set! name v) ...) does not work: v would have to
       ;; be a fresh pattern variable per name, and syntax-rules cannot invent them.
       (call-with-values
         (lambda () expr)
         (lambda produced
           (let walk ((setters (list (lambda (v) (set! name v)) ...))
                      (values-left produced))
             (if (and (pair? setters) (pair? values-left))
                 (begin ((car setters) (car values-left))
                        (walk (cdr setters) (cdr values-left)))
                 *unspecified*))))))))

(define-syntax receive
  (syntax-rules ()
    ((_ formals expr body ...)
     (call-with-values (lambda () expr) (lambda formals body ...)))))

;;; ---------------------------------------------------------------------------
;;; Guile-specific derived forms that LilyPond's Scheme uses
;;; ---------------------------------------------------------------------------

(define-syntax and-let*
  (syntax-rules ()
    ((_ ()) #t)
    ((_ () body ...) (begin body ...))
    ((_ ((var expr) rest ...) body ...)
     (let ((var expr)) (and var (and-let* (rest ...) body ...))))
    ((_ ((expr) rest ...) body ...)
     (and expr (and-let* (rest ...) body ...)))
    ((_ (var rest ...) body ...)
     (and var (and-let* (rest ...) body ...)))))

(define-syntax while
  (syntax-rules ()
    ((_ test body ...)
     (letrec ((loop (lambda () (if test (begin body ... (loop)) *unspecified*))))
       (loop)))))

(define-syntax begin0
  (syntax-rules ()
    ((_ first rest ...) (let ((result first)) rest ... result))))

;;; ---------------------------------------------------------------------------
;;; Promises
;;; ---------------------------------------------------------------------------

(define-syntax delay
  (syntax-rules ()
    ((_ expr) (make-promise-thunk (lambda () expr)))))

(define-syntax delay-force
  (syntax-rules ()
    ((_ expr) (make-promise-thunk (lambda () expr)))))

;;; ---------------------------------------------------------------------------
;;; Parameters, as SRFI-39 defines them, over the fluids the core provides
;;; ---------------------------------------------------------------------------

(define-syntax parameterize
  (syntax-rules ()
    ((_ ((param value) ...) body ...)
     (with-parameters* (list param ...) (list value ...) (lambda () body ...)))))

(define-syntax with-fluids
  (syntax-rules ()
    ((_ ((fluid value) ...) body ...)
     (with-fluids* (list fluid ...) (list value ...) (lambda () body ...)))))

;;; ---------------------------------------------------------------------------
;;; Assertions and misc
;;; ---------------------------------------------------------------------------

(define-syntax assert
  (syntax-rules ()
    ((_ expr) (if expr #t (error "assertion failed" 'expr)))
    ((_ expr message ...) (if expr #t (error message ...)))))

(define-syntax false-if-exception
  (syntax-rules ()
    ((_ expr) (catch #t (lambda () expr) (lambda args #f)))))

;;; boot-9.scm:2237 -- "Load a Scheme source file named NAME, searching for it in
;;; the directories listed in %load-path".  boot-9 is vendored but never loaded, so
;;; without this the name is simply unbound.
;;;
;;; It is Scheme rather than a C# primitive ON PURPOSE, and the reason is the same
;;; one boot-9 has for writing it this way: the call to primitive-load-path is
;;; resolved WHEN IT RUNS, so a host that replaces primitive-load-path is honoured.
;;; CodeBrix.LilyPort replaces exactly that name to serve LilyPond's Scheme layer
;;; out of embedded resources instead of a file system load path; a C# version
;;; holding its own reference to the core procedure would silently bypass the
;;; replacement and search a load path that has nothing in it.
;;;
;;; Guile wraps the call in (start-stack 'load-stack ...), which establishes a
;;; named stack frame for its debugger.  There is no such debugger here and the
;;; form has no other effect, so it is dropped rather than faked.
(define (load-from-path name)
  (primitive-load-path name))

;;; ---------------------------------------------------------------------------
;;; Module syntax
;;;
;;; LilyPond's .scm files open with (define-module ...) and (use-modules ...).
;;; These expand onto the C# module primitives.
;;; ---------------------------------------------------------------------------

(define-syntax use-modules
  (syntax-rules ()
    ((_ spec ...) (begin (use-one-module 'spec) ...))))

(define-syntax define-module
  (syntax-rules ()
    ((_ name clause ...) (define-module* 'name (list 'clause ...)))))

(define-syntax define-public
  (syntax-rules ()
    ;; Curried: (define-public ((f a) b) body) == (define (f a) (lambda (b) body))
    ((_ ((name . inner) . outer) body ...)
     (begin (define (name . inner) (lambda outer body ...)) (export-one 'name)))
    ((_ (name . args) body ...) (begin (define (name . args) body ...) (export-one 'name)))
    ((_ name value) (begin (define name value) (export-one 'name)))))

(define-syntax define*-public
  (syntax-rules ()
    ((_ (name . args) body ...) (begin (define* (name . args) body ...) (export-one 'name)))
    ((_ name value) (begin (define name value) (export-one 'name)))))

;;; let-keywords / let-keywords* -- (ice-9 optargs).
;;;
;;; That module is self-provided rather than autoloaded, and optargs.scm cannot be
;;; loaded verbatim: its let-keywords expands to `parse-lambda-case', a Guile VM
;;; primitive this implementation has no analogue for. lambda* already carries the whole
;;; keyword protocol in C#, so both macros are simply a lambda* applied to the rest list.
;;;
;;; The allow-other-keys flag is a literal at every real call site, so it is matched as a
;;; datum; anything else is treated as true, which is the safe direction (a keyword the
;;; binding list does not name is ignored rather than rejected).
;;;
;;; DIVERGENCE: lambda* binds sequentially, so a default expression can see the bindings
;;; before it -- which is let-keywords* semantics. Plain let-keywords evaluates its
;;; defaults in the enclosing scope. Nothing in the vendored layer writes a default that
;;; names an earlier binding, so the two agree there.
(define-syntax let-keywords
  (syntax-rules ()
    ((_ rest-arg #f (binding ...) b0 b1 ...)
     (apply (lambda* (#:key binding ...) b0 b1 ...) rest-arg))
    ((_ rest-arg aok (binding ...) b0 b1 ...)
     (apply (lambda* (#:key binding ... #:allow-other-keys) b0 b1 ...) rest-arg))))

(define-syntax let-keywords*
  (syntax-rules ()
    ((_ rest-arg #f (binding ...) b0 b1 ...)
     (apply (lambda* (#:key binding ...) b0 b1 ...) rest-arg))
    ((_ rest-arg aok (binding ...) b0 b1 ...)
     (apply (lambda* (#:key binding ... #:allow-other-keys) b0 b1 ...) rest-arg))))

(define-syntax export
  (syntax-rules ()
    ((_ name ...) (begin (export-one 'name) ...))))

(define-syntax re-export
  (syntax-rules ()
    ((_ name ...) (begin (export-one 'name) ...))))

(define-syntax export-syntax
  (syntax-rules ()
    ((_ name ...) (begin (export-one 'name) ...))))

(define-syntax defmacro-public
  (syntax-rules ()
    ((_ name args body ...)
     (begin (defmacro name args body ...) (export-one 'name)))))

;;; The docstring is PASSED THROUGH to syntax-rules, which is where psyntax puts
;;; it: expand-syntax-rules (ice-9/psyntax.scm:3186-3197) emits the transformer as
;;; (lambda (x) docstring ... (syntax-case ...)), so the string becomes the
;;; transformer procedure's documentation. Dropping it here compiled and ran
;;; identically and was invisible everywhere except one reader --
;;; document-functions.scm asks (procedure-documentation (macro-transformer m))
;;; and skips any macro that answers #f, so every macro LilyPond documents this
;;; way was silently absent from the Internals Reference.
(define-syntax define-syntax-rule
  (syntax-rules ()
    ((_ (name . pattern) template)
     (define-syntax name (syntax-rules () ((_ . pattern) template))))
    ((_ (name . pattern) docstring template)
     (define-syntax name (syntax-rules () docstring ((_ . pattern) template))))))

(define-syntax define-syntax-rule-public
  (syntax-rules ()
    ((_ (name . pattern) template)
     (begin (define-syntax-rule (name . pattern) template) (export-one 'name)))
    ((_ (name . pattern) docstring template)
     (begin (define-syntax-rule (name . pattern) docstring template) (export-one 'name)))))

;;; ---------------------------------------------------------------------------
;;; defmacro
;;;
;;; Guile's unhygienic macro form, still used by LilyPond -- notably for
;;; define-markup-command, which scm/markup-macros.scm defines with
;;; defmacro-public. Implemented on syntax-case in the standard way: strip the
;;; syntax objects with syntax->datum, run the ordinary procedure the user wrote,
;;; then re-wrap the result with datum->syntax against the macro use site, which
;;; is what makes the expansion unhygienic in exactly the way defmacro promises.
;;; ---------------------------------------------------------------------------

;;; A DOCSTRING GOES ON THE TRANSFORMER, NOT ON THE PROCEDURE THE USER WROTE.
;;; boot-9's define-macro (ice-9/boot-9.scm:735-757) emits (lambda (y) doc ...
;;; (syntax-case ...)) and hands the user's body to an inner lambda with the
;;; string removed. The distinction is invisible to every ordinary use of the
;;; macro and visible to exactly one reader: document-functions.scm documents a
;;; macro by asking procedure-documentation of (macro-transformer m), so a
;;; docstring left on the inner procedure means the macro is silently dropped
;;; from the manual. scm/markup-macros.scm defines define-markup-command this
;;; way, and it is one of the entries that went missing.
(define-syntax defmacro
  (lambda (x)
    (syntax-case x ()
      ((_ name args doc body1 body ...)
       (string? (syntax->datum #'doc))
       #'(define-syntax name
           (lambda (use)
             doc
             (syntax-case use ()
               ((_ . operands)
                (datum->syntax
                  use
                  (apply (lambda args body1 body ...)
                         (syntax->datum #'operands))))))))
      ((_ name args body ...)
       #'(define-syntax name
           (lambda (use)
             (syntax-case use ()
               ((_ . operands)
                (datum->syntax
                  use
                  (apply (lambda args body ...)
                         (syntax->datum #'operands)))))))))))

(define-syntax define-macro
  (syntax-rules ()
    ((_ (name . args) body ...) (defmacro name args body ...))
    ((_ name (lambda args body ...)) (defmacro name args body ...))))

;;; ---------------------------------------------------------------------------
;;; cond-expand and include
;;;
;;; LilyScheme presents a fixed feature set, so cond-expand resolves at expansion
;;; time against the features listed here. "guile" is claimed deliberately: the
;;; SRFI modules branch on it, and LilyScheme is a Guile dialect.
;;; ---------------------------------------------------------------------------

(define-syntax cond-expand
  (syntax-rules (and or not else guile guile-2 guile-3 srfi-0 srfi-1 srfi-2
                 srfi-6 srfi-8 srfi-9 srfi-11 srfi-13 srfi-16 srfi-23 srfi-30
                 srfi-39 srfi-46 srfi-55 srfi-61 srfi-62 srfi-87 r7rs)
    ((_ (else body ...)) (begin body ...))
    ((_ ((and) body ...) more ...) (begin body ...))
    ((_ ((and req1 req2 ...) body ...) more ...)
     (cond-expand (req1 (cond-expand ((and req2 ...) body ...) more ...)) more ...))
    ((_ ((or) body ...) more ...) (cond-expand more ...))
    ((_ ((or req1 req2 ...) body ...) more ...)
     (cond-expand (req1 (begin body ...)) ((or req2 ...) (begin body ...)) more ...))
    ((_ ((not req) body ...) more ...) (cond-expand (req (cond-expand more ...)) (else body ...)))
    ((_ (guile body ...) more ...) (begin body ...))
    ((_ (guile-2 body ...) more ...) (begin body ...))
    ((_ (guile-3 body ...) more ...) (begin body ...))
    ((_ (r7rs body ...) more ...) (begin body ...))
    ((_ (srfi-0 body ...) more ...) (begin body ...))
    ((_ (srfi-1 body ...) more ...) (begin body ...))
    ((_ (srfi-2 body ...) more ...) (begin body ...))
    ((_ (srfi-6 body ...) more ...) (begin body ...))
    ((_ (srfi-8 body ...) more ...) (begin body ...))
    ((_ (srfi-9 body ...) more ...) (begin body ...))
    ((_ (srfi-11 body ...) more ...) (begin body ...))
    ((_ (srfi-13 body ...) more ...) (begin body ...))
    ((_ (srfi-16 body ...) more ...) (begin body ...))
    ((_ (srfi-23 body ...) more ...) (begin body ...))
    ((_ (srfi-30 body ...) more ...) (begin body ...))
    ((_ (srfi-39 body ...) more ...) (begin body ...))
    ((_ (srfi-46 body ...) more ...) (begin body ...))
    ((_ (srfi-55 body ...) more ...) (begin body ...))
    ((_ (srfi-61 body ...) more ...) (begin body ...))
    ((_ (srfi-62 body ...) more ...) (begin body ...))
    ((_ (srfi-87 body ...) more ...) (begin body ...))
    ((_ (feature body ...) more ...) (cond-expand more ...))
    ((_) *unspecified*)))

(define-syntax include-from-path
  (syntax-rules ()
    ((_ path) (load-vendored path))))

(define-syntax include
  (syntax-rules ()
    ((_ path) (load-vendored path))))

;;; ---------------------------------------------------------------------------
;;; GOOPS -- define-class, define-method, and accessors
;;;
;;; Written against the documented GOOPS interface, NOT translated from Guile's
;;; oop/goops.scm. See GoopsPrimitives.cs for why, and for the measurement of
;;; LilyPond's actual usage that fixed the scope.
;;;
;;; define-class is a defmacro rather than syntax-rules because it has to walk the
;;; slot specifications as DATA -- reading #:accessor out of each one to decide
;;; which procedures to emit -- and syntax-rules cannot inspect a list that way.
;;; ---------------------------------------------------------------------------

(define (%slot-spec-name spec)
  (if (pair? spec) (car spec) spec))

(define (%slot-spec-option spec keyword)
  (if (pair? spec)
      (let loop ((rest (cdr spec)))
        (cond ((or (null? rest) (null? (cdr rest))) #f)
              ((eq? (car rest) keyword) (cadr rest))
              (else (loop (cddr rest)))))
      #f))

;;; A slot option's VALUE is an expression, and GOOPS evaluates it: goops.scm's
;;; `class' macro (line 1684) parses the option list and re-emits `(kw arg . rest)'
;;; with arg left in place to be evaluated, special-casing only #:init-form.
;;;
;;; Handing %make-class the whole slot specification as QUOTED DATA instead is a
;;; silent falsehood for exactly the options that carry a value. The one that bites
;;; is the commonest of all, #:init-value '() -- quoted, the slot is initialised to
;;; the two-element list (quote ()), which is a PAIR. Every (if (pair? slot) ...)
;;; guard in the consuming code then takes the wrong branch over a slot that is
;;; supposed to be empty: LilyPond's documentation-lib.scm builds a Texinfo @menu
;;; for a childless <texi-node>, reads a node name off the symbol `quote', gets #f
;;; from slot-ref, and dies in string-length with nothing pointing back to here.
;;;
;;; Only the options %make-class reads are evaluated. #:accessor, #:getter and
;;; #:setter name procedures this macro is itself about to define, and evaluating
;;; those names HERE -- before the defines below it -- would make every class
;;; definition fail on an unbound variable. Guile can evaluate them because its
;;; define-class emits the accessor definitions FIRST (define-class-pre-definitions);
;;; ours emits them after, so their names stay quoted. Recorded as a divergence.
(define (%slot-spec-option-expressions spec)
  (let loop ((rest (if (pair? spec) (cdr spec) '())))
    (cond ((or (null? rest) (null? (cdr rest))) '())
          ((or (eq? (car rest) #:init-value) (eq? (car rest) #:init-thunk))
           (cons (car rest) (cons (cadr rest) (loop (cddr rest)))))
          (else
           (cons (car rest)
                 (cons (list 'quote (cadr rest)) (loop (cddr rest))))))))

(define (%slot-spec->expression spec)
  (if (pair? spec)
      (cons 'list
            (cons (list 'quote (%slot-spec-name spec))
                  (%slot-spec-option-expressions spec)))
      (list 'quote spec)))

(defmacro define-class (name supers . slots)
  (let ((accessor-forms '()))
    (for-each
     (lambda (spec)
       (let ((slot-name (%slot-spec-name spec))
             (accessor (%slot-spec-option spec #:accessor))
             (getter (%slot-spec-option spec #:getter))
             (setter (%slot-spec-option spec #:setter)))
         ;; #:accessor makes one procedure that both reads and, given a second
         ;; argument, writes -- which is how LilyPond's documentation-lib uses it.
         ;;
         ;; It also CARRIES A SETTER, because GOOPS's #:accessor makes an <accessor>:
         ;; a generic whose setter is a generic, so (set! (acc obj) v) works. Emitting a
         ;; bare lambda instead reads identically everywhere the accessor is only called
         ;; -- and part-combiner.scm's (set! (split-index state) idx) then throws
         ;; wrong-type-arg on `setter`, taking the whole \partCombine family with it.
         (if accessor
             (set! accessor-forms
                   (cons `(define ,accessor
                            (make-procedure-with-setter
                             (lambda (object . value)
                               (if (null? value)
                                   (slot-ref object ',slot-name)
                                   (slot-set! object ',slot-name (car value))))
                             (lambda (object value)
                               (slot-set! object ',slot-name value))))
                         accessor-forms)))
         (if getter
             (set! accessor-forms
                   (cons `(define ,getter (lambda (object) (slot-ref object ',slot-name)))
                         accessor-forms)))
         (if setter
             (set! accessor-forms
                   (cons `(define ,setter
                            (lambda (object value) (slot-set! object ',slot-name value)))
                         accessor-forms)))))
     slots)
    `(begin
       (define ,name
         (%make-class ',name
                      (list ,@supers)
                      (list ,@(map %slot-spec->expression slots))))
       ,@(reverse accessor-forms)
       ',name)))

;;; (define-method (name (arg <class>) plain ...) body ...)
;;;
;;; A parameter written (arg <class>) is specialized on that class; a bare symbol
;;; accepts anything. The generic is created on first use and reused thereafter,
;;; so several define-methods with the same name accumulate.

(defmacro define-method (signature . body)
  (let* ((name (car signature))
         (params (cdr signature))
         (plain-params (map (lambda (p) (if (pair? p) (car p) p)) params))
         (specializers (map (lambda (p) (if (pair? p) (cadr p) #f)) params)))
    `(begin
       (%add-method! (%ensure-generic! ',name)
                     (list ,@specializers)
                     (lambda ,plain-params ,@body))
       ',name)))

(defmacro define-generic (name)
  `(begin (%ensure-generic! ',name) ',name))

;;; ---------------------------------------------------------------------------
;;; Curried definitions
;;;
;;; Guile's (ice-9 curried-definitions) shadows `define` so that
;;; (define ((f a) b) body) means (define (f a) (lambda (b) body)).
;;;
;;; That CANNOT be done here as a macro. psyntax resolves top-level identifiers at
;;; USE time, so a shadowing `define` whose expansion mentions `define` finds
;;; itself and recurses until the process aborts -- verified, exit code 134.
;;; The transform is instead applied in C# before expansion; see
;;; CurriedDefinitions.cs. define-public handles its own curried case above,
;;; because that macro is ours and can pattern-match directly.
;;; ---------------------------------------------------------------------------

;;; defmacro* is defmacro whose ARGUMENT LIST accepts lambda* options -- #:optional,
;;; #:key, #:rest. LilyPond's markup-macros.scm and markup.scm both rely on it, so
;;; the transformer body must be built with lambda*, not lambda.
(define-syntax defmacro*
  (lambda (x)
    (syntax-case x ()
      ;; The docstring is hoisted onto the TRANSFORMER, for the reason spelled out
      ;; at defmacro above: procedure-documentation of the macro-transformer is
      ;; what the manual reads. markup and markup-lambda are both defmacro*-public
      ;; with a docstring, and both were missing from the Internals Reference.
      ((_ name args doc body1 body ...)
       (string? (syntax->datum #'doc))
       #'(define-syntax name
           (lambda (use)
             doc
             (syntax-case use ()
               ((_ . operands)
                (datum->syntax
                  use
                  (apply (lambda* args body1 body ...)
                         (syntax->datum #'operands))))))))
      ((_ name args body ...)
       #'(define-syntax name
           (lambda (use)
             (syntax-case use ()
               ((_ . operands)
                (datum->syntax
                  use
                  (apply (lambda* args body ...)
                         (syntax->datum #'operands)))))))))))

(define-syntax defmacro*-public
  (syntax-rules ()
    ((_ name args body ...) (begin (defmacro* name args body ...) (export-one 'name)))))

;;; ---------------------------------------------------------------------------
;;; Option interfaces
;;;
;;; (debug-set! stack 0) NAMES its option rather than evaluating it, so these have
;;; to be syntax. LilyScheme has a single fixed behaviour, so setting an option is
;;; accepted and ignored -- but the option name must not be looked up as a variable.
;;; ---------------------------------------------------------------------------

(define-syntax debug-set!
  (syntax-rules () ((_ option value) *unspecified*)))

(define-syntax read-set!
  (syntax-rules () ((_ option value) *unspecified*)))

(define-syntax print-set!
  (syntax-rules () ((_ option value) *unspecified*)))

;;; ---------------------------------------------------------------------------
;;; quasisyntax
;;;
;;; Guile does not define quasisyntax in psyntax; boot-9.scm line 424 pulls it in
;;; with (include-from-path "ice-9/quasisyntax"), which is why it is part of the
;;; core environment rather than a module anyone imports. The prelude replaces
;;; boot-9, so the same include has to happen here or #` templates -- which
;;; LilyPond's scm/music-functions.scm is built on -- have no expander.
;;; ---------------------------------------------------------------------------

(include-from-path "ice-9/quasisyntax")

;;; ---------------------------------------------------------------------------
;;; the POSIX accessor layer
;;;
;;; Guile loads ice-9/posix.scm into the core the same way; it is pure Scheme
;;; defining the stat:, tm:, passwd:, group: and utsname: accessors over the
;;; vectors the C side builds. It is vendored VERBATIM; the pw/gr wrappers at
;;; its tail call getpw/getgr, which are unbound here -- calling one is a
;;; visible unbound-variable error, exactly the posture ABOUT boot-9 records.
;;; ---------------------------------------------------------------------------

(include-from-path "ice-9/posix")

;;; boot-9's own broken-down-time accessors (ice-9/boot-9.scm:2037-2061), copied
;;; verbatim: posix.scm carries the stat: family and boot-9 carries the tm:
;;; family, and boot-9 never loads here.

(define (tm:sec obj) (vector-ref obj 0))
(define (tm:min obj) (vector-ref obj 1))
(define (tm:hour obj) (vector-ref obj 2))
(define (tm:mday obj) (vector-ref obj 3))
(define (tm:mon obj) (vector-ref obj 4))
(define (tm:year obj) (vector-ref obj 5))
(define (tm:wday obj) (vector-ref obj 6))
(define (tm:yday obj) (vector-ref obj 7))
(define (tm:isdst obj) (vector-ref obj 8))
(define (tm:gmtoff obj) (vector-ref obj 9))
(define (tm:zone obj) (vector-ref obj 10))

(define (set-tm:sec obj val) (vector-set! obj 0 val))
(define (set-tm:min obj val) (vector-set! obj 1 val))
(define (set-tm:hour obj val) (vector-set! obj 2 val))
(define (set-tm:mday obj val) (vector-set! obj 3 val))
(define (set-tm:mon obj val) (vector-set! obj 4 val))
(define (set-tm:year obj val) (vector-set! obj 5 val))
(define (set-tm:wday obj val) (vector-set! obj 6 val))
(define (set-tm:yday obj val) (vector-set! obj 7 val))
(define (set-tm:isdst obj val) (vector-set! obj 8 val))
(define (set-tm:gmtoff obj val) (vector-set! obj 9 val))
(define (set-tm:zone obj val) (vector-set! obj 10 val))

;;; ---------------------------------------------------------------------------
;;; SRFI-9 records
;;;
;;; Guile builds define-record-type on make-record-type and the four procedures
;;; that derive a constructor, predicate, accessor and modifier from it. This
;;; does the same. It is a defmacro rather than syntax-rules because the field
;;; list has to be walked to pair each accessor with its field, which
;;; syntax-rules cannot express.
;;; ---------------------------------------------------------------------------

(defmacro define-record-type (type-name ctor-spec pred-name . field-specs)
  (let* ((type-symbol (if (pair? type-name) (car type-name) type-name))
         (fields (map (lambda (spec) (car spec)) field-specs)))
    `(begin
       (define ,type-symbol
         (make-record-type ',type-symbol ',fields))
       (define ,(car ctor-spec)
         (record-constructor ,type-symbol ',(cdr ctor-spec)))
       (define ,pred-name (record-predicate ,type-symbol))
       ,@(map
          (lambda (spec)
            (let ((field (car spec))
                  (rest (cdr spec)))
              `(begin
                 (define ,(car rest) (record-accessor ,type-symbol ',field))
                 ,@(if (pair? (cdr rest))
                       (list `(define ,(cadr rest)
                                (record-modifier ,type-symbol ',field)))
                       '()))))
          field-specs)
       ',type-symbol)))

;;; ---------------------------------------------------------------------------
;;; Exception printers for the keys thrown by Guile
;;;
;;; print-exception and set-exception-printer! are C# (ExceptionPrimitives);
;;; these are the standard per-key printers registered on top of them, copied
;;; from boot-9.scm:1917-1979 -- minus getaddrinfo-error, whose gai-strerror
;;; does not exist here. The vendored (ice-9 exceptions) registers its own
;;; '%exception printer over the same mechanism when it loads.
;;; ---------------------------------------------------------------------------

(let ()
  (define (scm-error-printer port key args default-printer)
    ;; Abuse case-lambda as a pattern matcher, given that we don't have
    ;; ice-9 match at this point.
    (apply (case-lambda
             ((subr msg args . rest)
              (if subr
                  (format port "In procedure ~a: " subr))
              (apply format port msg (or args '())))
             (_ (default-printer)))
           args))

  (define (syntax-error-printer port key args default-printer)
    (apply (case-lambda
             ((who what where form subform . extra)
              (format port "Syntax error:\n")
              (if where
                  (let ((file (or (assq-ref where 'filename) "unknown file"))
                        (line (and=> (assq-ref where 'line) 1+))
                        (col (assq-ref where 'column)))
                    (format port "~a:~a:~a: " file line col))
                  (format port "unknown location: "))
              (if who
                  (format port "~a: " who))
              (format port "~a" what)
              (if subform
                  (format port " in subform ~s of ~s" subform form)
                  (if form
                      (format port " in form ~s" form))))
             (_ (default-printer)))
           args))

  (define (keyword-error-printer port key args default-printer)
    (let ((message (cadr args))
          (faulty  (car (cadddr args)))) ; I won't do it again, I promise.
      (format port "~a: ~s" message faulty)))

  (set-exception-printer! 'goops-error scm-error-printer)
  (set-exception-printer! 'host-not-found scm-error-printer)
  (set-exception-printer! 'keyword-argument-error keyword-error-printer)
  (set-exception-printer! 'misc-error scm-error-printer)
  (set-exception-printer! 'no-data scm-error-printer)
  (set-exception-printer! 'no-recovery scm-error-printer)
  (set-exception-printer! 'null-pointer-error scm-error-printer)
  (set-exception-printer! 'out-of-memory scm-error-printer)
  (set-exception-printer! 'out-of-range scm-error-printer)
  (set-exception-printer! 'program-error scm-error-printer)
  (set-exception-printer! 'read-error scm-error-printer)
  (set-exception-printer! 'regular-expression-syntax scm-error-printer)
  (set-exception-printer! 'signal scm-error-printer)
  (set-exception-printer! 'stack-overflow scm-error-printer)
  (set-exception-printer! 'system-error scm-error-printer)
  (set-exception-printer! 'try-again scm-error-printer)
  (set-exception-printer! 'unbound-variable scm-error-printer)
  (set-exception-printer! 'wrong-number-of-args scm-error-printer)
  (set-exception-printer! 'wrong-type-arg scm-error-printer)

  (set-exception-printer! 'syntax-error syntax-error-printer))
