import * as path from "path";
import * as vscode from "vscode";
import { BoundedReferenceStore } from "./context-store";
import { supportsDiagnosticExplanation } from "./diagnostic-actions";
import type {
  DevFlowHostServices,
  DevFlowLayoutFinding,
  DevFlowLayoutReport,
  DevFlowProblem,
} from "./host-services";

interface DiagnosticReference {
  kind: "problem" | "layout";
  problemId?: string;
  findingId?: string;
  elementId?: string;
  sourceFile: string;
  sourceLine?: number | null;
  sourceColumn?: number | null;
}

function severityForProblem(problem: DevFlowProblem): vscode.DiagnosticSeverity {
  switch (problem.severity?.toLowerCase()) {
    case "error":
      return vscode.DiagnosticSeverity.Error;
    case "info":
    case "information":
      return vscode.DiagnosticSeverity.Information;
    case "hint":
      return vscode.DiagnosticSeverity.Hint;
    default:
      return vscode.DiagnosticSeverity.Warning;
  }
}

function severityForLayout(finding: DevFlowLayoutFinding): vscode.DiagnosticSeverity {
  if (finding.outcome === "violation" &&
      (finding.confidence === "high" || finding.confidence === "medium") &&
      finding.actionability !== "none") {
    return vscode.DiagnosticSeverity.Warning;
  }
  return finding.outcome === "incomplete"
    ? vscode.DiagnosticSeverity.Hint
    : vscode.DiagnosticSeverity.Information;
}

function diagnosticRange(line?: number | null, column?: number | null): vscode.Range {
  const startLine = Math.max(0, (line ?? 1) - 1);
  const startColumn = Math.max(0, (column ?? 1) - 1);
  return new vscode.Range(startLine, startColumn, startLine, startColumn + 1);
}

async function resolveWorkspaceSource(sourceFile: string): Promise<vscode.Uri | null> {
  if (!sourceFile || sourceFile.length > 1024) return null;
  const cached = workspaceSourceCache.get(sourceFile);
  if (cached !== undefined) return cached;
  const resolved = await resolveWorkspaceSourceUncached(sourceFile);
  // A publish resolves one path per finding, and the same handful of files recur across findings
  // and across refreshes. Without this every tick re-globs the workspace hundreds of times. The
  // cache is bounded and cleared whenever workspace folders change.
  if (workspaceSourceCache.size >= 2048) workspaceSourceCache.clear();
  workspaceSourceCache.set(sourceFile, resolved);
  return resolved;
}

const workspaceSourceCache = new Map<string, vscode.Uri | null>();

async function resolveWorkspaceSourceUncached(sourceFile: string): Promise<vscode.Uri | null> {
  const direct = vscode.Uri.file(sourceFile);
  if (path.isAbsolute(sourceFile) && vscode.workspace.getWorkspaceFolder(direct)) {
    try {
      await vscode.workspace.fs.stat(direct);
      return direct;
    } catch {
      return null;
    }
  }

  const basename = path.basename(sourceFile.replace(/^pack:\/\//i, ""));
  if (!basename || basename === "." || basename === path.sep) return null;
  const matches = await vscode.workspace.findFiles(`**/${basename}`, "**/{bin,obj,node_modules}/**", 2);
  return matches.length === 1 ? matches[0] : null;
}

export class DevFlowDiagnosticsController implements vscode.CodeActionProvider, vscode.Disposable {
  static readonly providedCodeActionKinds = [vscode.CodeActionKind.QuickFix];

  private readonly collection = vscode.languages.createDiagnosticCollection("maui-devflow");
  // Capacity covers both publish caps below (500 problems + 500 layout findings). A smaller store
  // would evict tokens during the very publish loop that created them, leaving quick fixes that
  // report an expired reference the moment they are clicked.
  private readonly references = new BoundedReferenceStore<DiagnosticReference>(1024, 1024 * 1024);
  private readonly problemDiagnostics = new Map<string, { uri: vscode.Uri; diagnostics: vscode.Diagnostic[] }>();
  private readonly layoutDiagnostics = new Map<string, { uri: vscode.Uri; diagnostics: vscode.Diagnostic[] }>();
  private disposed = false;

  constructor(
    private readonly context: vscode.ExtensionContext,
    private readonly services: DevFlowHostServices,
  ) {
    context.subscriptions.push(
      this.collection,
      vscode.workspace.onDidChangeWorkspaceFolders(() => workspaceSourceCache.clear()),
      vscode.languages.registerCodeActionsProvider(
        [{ language: "xml", scheme: "file" }, { language: "xaml", scheme: "file" }, { language: "csharp", scheme: "file" }],
        this,
        { providedCodeActionKinds: DevFlowDiagnosticsController.providedCodeActionKinds },
      ),
      vscode.commands.registerCommand("mauiDevflow.inspectDiagnostic", async (token: string) => {
        const reference = this.references.get(token);
        if (!reference) {
          vscode.window.showWarningMessage("This DevFlow diagnostic reference expired.");
          return;
        }
        await services.openInspector({
          element: reference.elementId,
          problem: reference.problemId,
          view: reference.kind === "problem" ? "problems" : "layout",
        });
      }),
      vscode.commands.registerCommand("mauiDevflow.explainDiagnostic", async (token: string) => {
        const reference = this.references.get(token);
        if (!reference) {
          vscode.window.showWarningMessage("This DevFlow diagnostic reference expired.");
          return;
        }
        const query = reference.kind === "layout" && reference.findingId
          ? `Explain the DevFlow layout finding ${reference.findingId}.`
          : reference.problemId
            ? `Explain the DevFlow runtime problem ${reference.problemId}.`
            : null;
        if (!query) {
          vscode.window.showWarningMessage("This DevFlow diagnostic cannot be explained.");
          return;
        }
        await vscode.commands.executeCommand("workbench.action.chat.open", {
          query,
          isPartialQuery: false,
        });
      }),
      vscode.commands.registerCommand("mauiDevflow.openSelectedRuntimeSource", async () => {
        const selection = services.getSelectedElement();
        if (!selection?.sourceFile) {
          vscode.window.showWarningMessage("The selected live element has no current source mapping.");
          return;
        }
        const uri = await resolveWorkspaceSource(selection.sourceFile);
        if (!uri) {
          vscode.window.showWarningMessage("The selected element's source could not be resolved uniquely in this workspace.");
          return;
        }
        const document = await vscode.workspace.openTextDocument(uri);
        await vscode.window.showTextDocument(document, {
          selection: diagnosticRange(selection.sourceLine, selection.sourceColumn),
        });
      }),
    );
  }

  async refreshProblems(elementId?: string): Promise<void> {
    if (this.disposed) return;
    if (!vscode.workspace.getConfiguration("mauiDevflow").get<boolean>("publishDiagnostics", false)) {
      this.problemDiagnostics.clear();
      this.layoutDiagnostics.clear();
      this.collection.clear();
      return;
    }
    const batch = await this.services.getProblems(elementId);
    if (!batch) return;
    const grouped = new Map<string, { uri: vscode.Uri; diagnostics: vscode.Diagnostic[] }>();
    for (const problem of batch.problems.slice(0, 500)) {
      if (!problem.sourceFile) continue;
      const uri = await resolveWorkspaceSource(problem.sourceFile);
      if (!uri) continue;
      const token = this.references.put({
        kind: "problem",
        problemId: problem.id,
        elementId: problem.elementId ?? undefined,
        sourceFile: uri.fsPath,
        sourceLine: problem.sourceLine,
        sourceColumn: problem.sourceColumn,
      });
      const diagnostic = new vscode.Diagnostic(
        diagnosticRange(problem.sourceLine, problem.sourceColumn),
        `${problem.message}${(problem.count ?? 1) > 1 ? ` (${problem.count} occurrences)` : ""}`,
        severityForProblem(problem),
      );
      diagnostic.source = "MAUI DevFlow";
      diagnostic.code = problem.code || problem.kind || "runtime-problem";
      diagnostic.tags = [];
      Object.defineProperty(diagnostic, "__devflowToken", { value: token, enumerable: false });
      const key = uri.toString();
      const entry = grouped.get(key) ?? { uri, diagnostics: [] };
      entry.diagnostics.push(diagnostic);
      grouped.set(key, entry);
    }
    this.problemDiagnostics.clear();
    for (const [key, entry] of grouped) this.problemDiagnostics.set(key, entry);
    this.rebuildCollection();
  }

  async publishLayout(report: DevFlowLayoutReport): Promise<void> {
    if (this.disposed ||
        !vscode.workspace.getConfiguration("mauiDevflow").get<boolean>("publishDiagnostics", false)) return;
    const findings = new Map<string, { uri: vscode.Uri; diagnostics: vscode.Diagnostic[] }>();
    for (const finding of (report.findings ?? []).slice(0, 500)) {
      if (!finding.element?.sourceFile || !finding.message) continue;
      const uri = await resolveWorkspaceSource(finding.element.sourceFile);
      if (!uri) continue;
      const diagnostic = new vscode.Diagnostic(
        diagnosticRange(finding.element.sourceLine, finding.element.sourceColumn),
        `${finding.message} [${finding.outcome ?? "observation"}, ${finding.confidence ?? "unknown"} confidence]`,
        severityForLayout(finding),
      );
      const token = this.references.put({
        kind: "layout",
        findingId: finding.id,
        elementId: finding.element.id,
        sourceFile: uri.fsPath,
        sourceLine: finding.element.sourceLine,
        sourceColumn: finding.element.sourceColumn,
      });
      diagnostic.source = "MAUI DevFlow Layout";
      diagnostic.code = finding.ruleId || "layout";
      Object.defineProperty(diagnostic, "__devflowToken", { value: token, enumerable: false });
      const key = uri.toString();
      const entry = findings.get(key) ?? { uri, diagnostics: [] };
      entry.diagnostics.push(diagnostic);
      findings.set(key, entry);
    }
    this.layoutDiagnostics.clear();
    for (const [key, entry] of findings) this.layoutDiagnostics.set(key, entry);
    this.rebuildCollection();
  }

  provideCodeActions(
    _document: vscode.TextDocument,
    _range: vscode.Range | vscode.Selection,
    context: vscode.CodeActionContext,
  ): vscode.CodeAction[] {
    const actions: vscode.CodeAction[] = [];
    for (const diagnostic of context.diagnostics) {
      if (!diagnostic.source?.startsWith("MAUI DevFlow")) continue;
      const token = (diagnostic as vscode.Diagnostic & { __devflowToken?: string }).__devflowToken;
      if (token) {
        const inspect = new vscode.CodeAction("Inspect live control", vscode.CodeActionKind.QuickFix);
        inspect.command = { command: "mauiDevflow.inspectDiagnostic", title: inspect.title, arguments: [token] };
        inspect.diagnostics = [diagnostic];
        actions.push(inspect);

        const reference = this.references.get(token);
        if (reference && supportsDiagnosticExplanation(reference.kind)) {
          const explain = new vscode.CodeAction("Explain with Copilot", vscode.CodeActionKind.QuickFix);
          explain.command = { command: "mauiDevflow.explainDiagnostic", title: explain.title, arguments: [token] };
          explain.diagnostics = [diagnostic];
          actions.push(explain);
        }
      }
    }

    const selection = this.services.getSelectedElement();
    if (selection?.sourceFile) {
      const openSource = new vscode.CodeAction("Open selected runtime element", vscode.CodeActionKind.QuickFix);
      openSource.command = {
        command: "mauiDevflow.openSelectedRuntimeSource",
        title: openSource.title,
      };
      actions.push(openSource);
    }
    return actions;
  }

  dispose(): void {
    this.disposed = true;
    this.collection.dispose();
    this.references.clear();
  }

  private rebuildCollection(): void {
    const keys = new Set([
      ...this.problemDiagnostics.keys(),
      ...this.layoutDiagnostics.keys(),
    ]);
    this.collection.clear();
    for (const key of keys) {
      const problems = this.problemDiagnostics.get(key);
      const layout = this.layoutDiagnostics.get(key);
      const uri = problems?.uri ?? layout?.uri;
      if (!uri) continue;
      this.collection.set(uri, [
        ...(problems?.diagnostics ?? []),
        ...(layout?.diagnostics ?? []),
      ]);
    }
  }
}
