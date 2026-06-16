/**
 * Renders parameters and injections for a selected component node.
 */
import type {
  ComponentInjection,
  ComponentNode,
  ComponentParameter,
} from "../../types/protocol.js";

/** Maximum characters shown for a serialized parameter value. */
const MAX_VALUE_CHARS = 160;

/**
 * Renders the details pane for a selected component, or a placeholder when none is selected.
 *
 * @param container - DOM element that receives detail content (typically `#details-root`).
 * @param node - Selected component node, or `null` when nothing is selected.
 * @returns Nothing.
 */
export const renderDetails = (
  container: HTMLElement,
  node: ComponentNode | null,
): void => {
  container.replaceChildren();

  if (!node) {
    return;
  }

  const header = document.createElement("p");
  header.className = "details-header";
  header.textContent = node.name;
  container.appendChild(header);

  if (node.locator) {
    const locatorRow = document.createElement("p");
    locatorRow.className = "details-locator";
    locatorRow.textContent = `Locator: ${node.locator}`;
    container.appendChild(locatorRow);
  } else {
    const locatorRow = document.createElement("p");
    locatorRow.className = "details-locator details-locator-missing";
    locatorRow.textContent = "Locator: (none — no highlightable DOM element)";
    container.appendChild(locatorRow);
  }

  const parameters = node.parameters ?? [];
  const injections = node.injections ?? [];

  container.appendChild(renderSection("Parameters", parameters, renderParameterTable));
  container.appendChild(renderSection("Injections", injections, renderInjectionTable));
};

/**
 * Builds a named details section with a table or empty placeholder.
 *
 * @param title - Section heading text.
 * @param items - Rows to display in the section table.
 * @param renderTable - Factory that builds the table element from items.
 * @returns The section wrapper element.
 */
const renderSection = <T>(
  title: string,
  items: readonly T[],
  renderTable: (rows: readonly T[]) => HTMLElement,
): HTMLElement => {
  const section = document.createElement("section");
  section.className = "details-section";

  const heading = document.createElement("h3");
  heading.textContent = title;
  section.appendChild(heading);

  if (items.length === 0) {
    const empty = document.createElement("p");
    empty.className = "details-empty-section";
    empty.textContent = `No ${title.toLowerCase()}.`;
    section.appendChild(empty);
    return section;
  }

  section.appendChild(renderTable(items));
  return section;
};

/**
 * Builds the parameters table for the details pane.
 *
 * @param parameters - Parameter rows from the selected node.
 * @returns Table element with name, type, and value columns.
 */
const renderParameterTable = (
  parameters: readonly ComponentParameter[],
): HTMLElement => {
  const table = createTable(["Name", "Type", "Value"]);

  for (const param of parameters) {
    const row = document.createElement("tr");

    appendCell(row, "col-name", param.name);
    appendCell(row, "col-type", prettifyClrType(param.typeName));
    appendCell(row, "", formatValue(param.value));

    table.querySelector("tbody")!.appendChild(row);
  }

  return table;
};

/**
 * Builds the injections table for the details pane.
 *
 * @param injections - Injection rows from the selected node.
 * @returns Table element with name, service type, and implementation columns.
 */
const renderInjectionTable = (
  injections: readonly ComponentInjection[],
): HTMLElement => {
  const table = createTable(["Name", "Service type", "Implementation"]);

  for (const injection of injections) {
    const row = document.createElement("tr");

    appendCell(row, "col-name", injection.name);
    appendCell(row, "col-type", prettifyClrType(injection.serviceType));
    appendCell(
      row,
      "",
      injection.implementationType ?? "(unresolved)",
    );

    table.querySelector("tbody")!.appendChild(row);
  }

  return table;
};

/**
 * Creates a details table with the given column headers.
 *
 * @param headers - Column header labels.
 * @returns Initialized table element with thead and tbody.
 */
const createTable = (headers: readonly string[]): HTMLTableElement => {
  const table = document.createElement("table");
  table.className = "details-table";

  const thead = document.createElement("thead");
  const headRow = document.createElement("tr");

  for (const header of headers) {
    const th = document.createElement("th");
    th.textContent = header;
    headRow.appendChild(th);
  }

  thead.appendChild(headRow);
  table.appendChild(thead);
  table.appendChild(document.createElement("tbody"));

  return table;
};

/**
 * Appends a table cell with optional column class.
 *
 * @param row - Table row receiving the cell.
 * @param className - Optional CSS class for the cell.
 * @param text - Cell text content.
 * @returns Nothing.
 */
const appendCell = (
  row: HTMLTableRowElement,
  className: string,
  text: string,
): void => {
  const cell = document.createElement("td");
  if (className) {
    cell.className = className;
  }
  cell.textContent = text;
  row.appendChild(cell);
};

/**
 * Shortens a CLR type name for display (lossy for nested generics).
 *
 * @param typeName - Full CLR type name from the protocol payload.
 * @returns A shortened type label safe for display.
 */
const prettifyClrType = (typeName: string): string => {
  try {
    const withoutNamespace = typeName.includes(".")
      ? (typeName.split(".").pop() ?? typeName)
      : typeName;
    const backtickIndex = withoutNamespace.indexOf("`");
    return backtickIndex >= 0
      ? withoutNamespace.slice(0, backtickIndex)
      : withoutNamespace;
  } catch {
    return typeName;
  }
};

/**
 * Formats a parameter value for display in the details table.
 *
 * @param value - Serialized parameter value from the protocol payload.
 * @returns A safe, truncated string representation.
 */
const formatValue = (value: unknown): string => {
  if (value === undefined) {
    return "undefined";
  }

  if (value === null) {
    return "null";
  }

  if (typeof value === "string") {
    return truncate(`"${value}"`);
  }

  if (typeof value === "boolean" || typeof value === "number") {
    return String(value);
  }

  try {
    const serialized = JSON.stringify(value);
    if (serialized === undefined) {
      return "undefined";
    }
    return truncate(serialized);
  } catch {
    return "<unserializable>";
  }
};

/**
 * Truncates a string to {@link MAX_VALUE_CHARS} with an ellipsis suffix.
 *
 * @param text - Input string to truncate.
 * @returns Truncated string when longer than the limit.
 */
const truncate = (text: string): string => {
  if (text.length <= MAX_VALUE_CHARS) {
    return text;
  }
  return `${text.slice(0, MAX_VALUE_CHARS)}…`;
};
