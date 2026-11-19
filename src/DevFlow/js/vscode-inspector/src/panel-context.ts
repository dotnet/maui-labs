export class PanelContextStore<TSelection, TSnapshot> {
  private readonly panels = new Map<object, {
    selection: TSelection | null;
    snapshot: TSnapshot | null;
  }>();
  private activeOwner: object | null = null;

  add(owner: object): void {
    this.panels.set(owner, { selection: null, snapshot: null });
  }

  activate(owner: object): void {
    if (this.panels.has(owner)) this.activeOwner = owner;
  }

  get selection(): TSelection | null {
    return this.activeOwner ? this.getSelection(this.activeOwner) : null;
  }

  get snapshot(): TSnapshot | null {
    return this.activeOwner ? this.panels.get(this.activeOwner)?.snapshot ?? null : null;
  }

  getSelection(owner: object): TSelection | null {
    return this.panels.get(owner)?.selection ?? null;
  }

  setSelection(owner: object, selection: TSelection | null): void {
    const panel = this.panels.get(owner);
    if (panel) panel.selection = selection;
  }

  setSnapshot(owner: object, snapshot: TSnapshot): void {
    const panel = this.panels.get(owner);
    if (panel) panel.snapshot = snapshot;
  }

  clear(owner: object): void {
    const panel = this.panels.get(owner);
    if (panel) {
      panel.selection = null;
      panel.snapshot = null;
    }
  }

  remove(owner: object): void {
    this.panels.delete(owner);
    if (this.activeOwner === owner) this.activeOwner = null;
  }
}
