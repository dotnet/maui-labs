/**
 * Argument-vector shaping for the pinned Mobile Canvas companion CLI.
 *
 * Every value that reaches this layer was chosen by a model, so a token beginning with "-" is a
 * realistic input rather than a hypothetical one. `actions.mjs` places positional values behind a
 * "--" end-of-options marker; this module holds the one rule that has to hold on the other side of
 * that marker.
 */

/**
 * Inserts the CLI's own `--json` flag ahead of the end-of-options marker.
 *
 * Appending it blindly would place it after the marker, where it is a positional argument and not a
 * flag: the command would emit human-readable output that `JSON.parse` then rejects, and the
 * failure would look like a broken companion rather than a broken argv. `indexOf` finds the first
 * `--`, which is always the marker, because every token before it is one this extension wrote and
 * none of those is a bare `--`.
 */
export function withJson(args) {
  const marker = args.indexOf("--");
  return marker < 0
    ? [...args, "--json"]
    : [...args.slice(0, marker), "--json", ...args.slice(marker)];
}
