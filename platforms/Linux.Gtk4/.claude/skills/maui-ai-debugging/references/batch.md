# Batch Command Reference

Execute multiple MAUI/cdp commands in a single CLI invocation via stdin. Outputs JSONL
responses (one JSON object per line) to stdout — ideal for AI agents and scripting.

## Usage

```bash
# Pipe multiple commands (semicolons or newlines as separators)
echo "MAUI fill textUsername user; MAUI fill textPassword pwd123; MAUI tap buttonLogin" | maui-devflow batch

# Multi-line input
printf "MAUI status\nMAUI tree\nMAUI screenshot --output screen.png" | maui-devflow batch

# With options
echo "MAUI status; MAUI tree" | maui-devflow batch --delay 500 --continue-on-error --agent-port 10224

# Human-readable output instead of JSONL
echo "MAUI status; MAUI tree" | maui-devflow batch --human
```

## Options

| Option | Default | Description |
|--------|---------|-------------|
| `--delay <ms>` | 250 | Delay between commands (lets UI settle) |
| `--continue-on-error` | false | Continue after a command fails (default: stop) |
| `--human` | false | Human-readable output instead of JSONL |

## JSONL Response Format

One JSON object per command, streamed as each completes:
```json
{"command":"MAUI fill textUsername user","exit_code":0,"output":"Filled: textUsername"}
{"command":"MAUI tap buttonLogin","exit_code":1,"output":"Error: Element not found: buttonLogin"}
```

## Interactive Streaming

The batch command processes stdin line-by-line, so a caller can read each JSONL response
before sending the next command. This enables reactive workflows where the AI agent inspects
results and decides the next action.

## Input Rules

- Lines starting with `#` are comments (skipped)
- Empty lines are skipped
- Semicolons separate multiple commands on one line
- Quoted strings are preserved: `MAUI fill myEntry "hello world"`
- Only `MAUI` and `cdp` commands are allowed (broker/list/etc. are rejected)
