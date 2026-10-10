import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import gsap from 'gsap'
import { ScrollTrigger } from 'gsap/ScrollTrigger'
import {
  Activity,
  ArrowDown,
  ArrowUpRight,
  Bot,
  Bug,
  Camera,
  Check,
  ChevronDown,
  ChevronRight,
  CircleDot,
  Clipboard,
  Code2,
  Cpu,
  Database,
  Eye,
  FileSearch,
  Gauge,
  Github,
  Keyboard,
  Layers3,
  Menu,
  MousePointer2,
  Network,
  Play,
  Puzzle,
  Radio,
  RotateCw,
  Route,
  ScrollText,
  ShoppingBag,
  Smartphone,
  TerminalSquare,
  Video,
  Wifi,
  X,
  Zap,
} from 'lucide-react'

gsap.registerPlugin(ScrollTrigger)

const navItems = [
  { label: 'Capabilities', href: '#tools', icon: Layers3 },
  { label: 'How it works', href: '#protocol', icon: Route },
  { label: 'Setup', href: '#setup', icon: TerminalSquare },
]

const loopSteps = [
  {
    label: 'Inspect the screen',
    command: 'maui_tree',
    detail: 'Read live MAUI views and stable AutomationIds.',
    agentStatus: 'Inspecting the screen…',
    proof: '7 nodes',
    transfer: 'tree',
    icon: Layers3,
  },
  {
    label: 'Operate the app',
    command: 'maui_tap',
    detail: 'Tap Checkout on the running native control.',
    agentStatus: 'Operating the app…',
    proof: 'tap sent',
    transfer: 'tap',
    icon: MousePointer2,
  },
  {
    label: 'Find the failure',
    command: 'maui_network_detail',
    detail: 'Catch the 500 response and the matching app log.',
    agentStatus: 'Tracing the failure…',
    proof: '500 traced',
    transfer: 'network',
    icon: Bug,
  },
  {
    label: 'Verify the fix',
    command: 'maui_screenshot',
    detail: 'Run the flow again and confirm the success state.',
    agentStatus: 'Verifying the fix…',
    proof: 'captured',
    transfer: 'screenshot',
    icon: Check,
  },
]

const heroPrompt = 'Fix the checkout flow, then verify it in the app.'

const toolGroups = [
  {
    verb: 'SEE',
    title: 'Read the live app',
    copy: 'Your agent gets structured visual context instead of guessing from source.',
    tools: [
      { label: 'Visual tree', icon: Layers3 },
      { label: 'Element details', icon: Eye },
      { label: 'Hit testing', icon: CircleDot },
      { label: 'Screenshots', icon: Camera },
      { label: 'Screen recording', icon: Video },
    ],
    icon: Eye,
  },
  {
    verb: 'ACT',
    title: 'Drive real controls',
    copy: 'Tap, type, scroll, navigate, resize, dismiss dialogs, and batch complete flows.',
    tools: [
      { label: 'Tap & fill', icon: MousePointer2 },
      { label: 'Scroll', icon: ScrollText },
      { label: 'Shell navigation', icon: Route },
      { label: 'Keys & focus', icon: Keyboard },
      { label: 'Batch operations', icon: Play },
    ],
    icon: MousePointer2,
  },
  {
    verb: 'INSPECT',
    title: 'Trace what happened',
    copy: 'The same agent that changed the code can inspect the evidence from the running app.',
    tools: [
      { label: 'Logs', icon: ScrollText },
      { label: 'Network', icon: Network },
      { label: 'Profiler', icon: Gauge },
      { label: 'Device & sensors', icon: Activity },
      { label: 'Storage', icon: Database },
    ],
    icon: FileSearch,
  },
  {
    verb: 'EXTEND',
    title: 'Expose app-specific truth',
    copy: 'Add custom actions and diagnostic endpoints that become discoverable CLI and MCP tools.',
    tools: [
      { label: 'MCP server', icon: Bot },
      { label: 'Agent actions', icon: Zap },
      { label: 'Extensions', icon: Puzzle },
      { label: 'Driver API', icon: Code2 },
      { label: 'Blazor CDP', icon: Cpu },
    ],
    icon: Zap,
  },
]

const manualSetupSteps = [
  {
    number: '01',
    title: 'Wire DevFlow into Debug builds',
    description: 'Add the agent package, then register it where your MAUI app is created.',
    command: 'dotnet add package Microsoft.Maui.DevFlow.Agent --prerelease',
    code: `#if DEBUG
builder.AddMauiDevFlowAgent();
#endif`,
  },
  {
    number: '02',
    title: 'Install the MAUI CLI and skills',
    description: 'Install the unified tool once, then add the project-scoped skills your coding agent can use.',
    command: 'dotnet tool install -g Microsoft.Maui.Cli --prerelease',
    code: 'maui devflow init',
  },
  {
    number: '03',
    title: 'Run the app. Close the loop.',
    description: 'Discover the live agent, inspect the first tree, then expose the same surface over MCP.',
    command: 'maui devflow diagnose && maui devflow wait',
    code: 'maui devflow ui tree --depth 1\nmaui devflow mcp',
  },
]

const quickSetupSteps = [
  {
    number: '01',
    label: 'Install the MAUI CLI',
    value: 'dotnet tool install -g Microsoft.Maui.Cli --prerelease',
    type: 'command',
  },
  {
    number: '02',
    label: 'Initialize DevFlow',
    value: 'maui devflow init',
    type: 'command',
  },
  {
    number: '03',
    label: 'Ask your coding agent',
    value: 'Add MAUI DevFlow to my app',
    type: 'prompt',
  },
]

function useReducedMotion() {
  const [reduced, setReduced] = useState(false)

  useEffect(() => {
    const media = window.matchMedia('(prefers-reduced-motion: reduce)')
    const update = () => setReduced(media.matches)
    update()
    media.addEventListener('change', update)
    return () => media.removeEventListener('change', update)
  }, [])

  return reduced
}

function DevFlowMark({ className = '' }) {
  return (
    <svg className={className} viewBox="0 0 48 48" aria-hidden="true">
      <rect x="1" y="1" width="46" height="46" rx="14" fill="currentColor" />
      <path
        d="M9 28h8l4-13 6 20 4-13h8"
        fill="none"
        stroke="var(--color-white)"
        strokeWidth="3.5"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  )
}

function MagneticLink({ href, children, variant = 'primary', external = false }) {
  return (
    <a
      className={`button button--${variant}`}
      href={href}
      target={external ? '_blank' : undefined}
      rel={external ? 'noreferrer' : undefined}
    >
      <span className="button__wash" aria-hidden="true" />
      <span className="button__content">{children}</span>
    </a>
  )
}

function Navbar({ activeSection }) {
  const [open, setOpen] = useState(false)

  useEffect(() => {
    if (!open) return undefined
    const closeOnEscape = (event) => {
      if (event.key === 'Escape') setOpen(false)
    }
    window.addEventListener('keydown', closeOnEscape)
    return () => window.removeEventListener('keydown', closeOnEscape)
  }, [open])

  return (
    <header className="navbar" data-navbar>
      <a className="navbar__brand" href="#top" aria-label=".NET MAUI DevFlow home">
        <DevFlowMark className="navbar__mark" />
        <span className="navbar__wordmark">
          <span className="navbar__wordmark-dotnet">.NET</span>
          <span className="navbar__wordmark-maui">MAUI</span>
          <span className="navbar__wordmark-product">
            <span className="navbar__wordmark-dev">Dev</span>
            <span className="navbar__wordmark-flow">Flow</span>
          </span>
        </span>
      </a>

      <nav className="navbar__desktop" aria-label="Primary navigation">
        {navItems.map((item) => {
          const Icon = item.icon
          return (
            <a
              key={item.href}
              className={activeSection === item.href.slice(1) ? 'is-active' : ''}
              href={item.href}
              aria-current={activeSection === item.href.slice(1) ? 'location' : undefined}
            >
              <Icon size={16} aria-hidden="true" />
              {item.label}
            </a>
          )
        })}
      </nav>

      <a className="navbar__cta" href="#setup">
        Get Started
        <ArrowDown size={16} aria-hidden="true" />
      </a>

      <button
        className="navbar__menu"
        type="button"
        aria-expanded={open}
        aria-controls="mobile-menu"
        aria-label={open ? 'Close navigation menu' : 'Open navigation menu'}
        onClick={() => setOpen((value) => !value)}
      >
        {open ? <X size={22} /> : <Menu size={22} />}
      </button>

      <nav id="mobile-menu" className={`navbar__mobile ${open ? 'is-open' : ''}`} aria-label="Mobile navigation">
        {navItems.map((item) => {
          const Icon = item.icon
          return (
            <a key={item.href} href={item.href} onClick={() => setOpen(false)}>
              <span className="navbar__mobile-label">
                <Icon size={18} aria-hidden="true" />
                {item.label}
              </span>
              <ChevronRight size={18} aria-hidden="true" />
            </a>
          )
        })}
        <a href="#setup" onClick={() => setOpen(false)}>
          Get Started
          <ArrowDown size={18} aria-hidden="true" />
        </a>
      </nav>
    </header>
  )
}

function StepTransfer({ type }) {
  if (type === 'tree') {
    return (
      <div className="transfer-tree">
        <span className="transfer-tree__title">
          <Layers3 size={13} />
          live visual tree
        </span>
        <div className="transfer-tree__branch">
          <span>Page</span>
          <span>Grid #CartItem</span>
          <span>Button #Checkout</span>
        </div>
      </div>
    )
  }

  if (type === 'tap') {
    return (
      <div className="transfer-tap">
        <MousePointer2 size={18} />
        <span>
          <b>maui_tap</b>
          #Checkout
        </span>
      </div>
    )
  }

  if (type === 'network') {
    return (
      <div className="transfer-network">
        <span className="transfer-network__route">
          <Network size={13} />
          POST /checkout
        </span>
        <span className="transfer-network__status">500</span>
        <span className="transfer-network__trace">InvalidOperationException</span>
        <span className="transfer-network__packet" />
      </div>
    )
  }

  return (
    <div className="transfer-screenshot">
      <span className="transfer-screenshot__bar">
        <ShoppingBag size={10} />
        Trail Shop
      </span>
      <span className="transfer-screenshot__success">
        <Check size={22} />
      </span>
      <strong>Order confirmed</strong>
      <small>checkout-fixed.png</small>
    </div>
  )
}

function HeroLoop({ reducedMotion }) {
  const [activeStep, setActiveStep] = useState(reducedMotion ? 0 : null)
  const [typedPrompt, setTypedPrompt] = useState(reducedMotion ? heroPrompt : '')
  const [promptSubmitted, setPromptSubmitted] = useState(reducedMotion)
  const [agentComplete, setAgentComplete] = useState(false)
  const [introAlertVisible, setIntroAlertVisible] = useState(!reducedMotion)
  const [terminalResetting, setTerminalResetting] = useState(false)
  const [cycle, setCycle] = useState(0)
  const loopRef = useRef(null)
  const screenRef = useRef(null)
  const checkoutRef = useRef(null)
  const transferRef = useRef(null)
  const proofRefs = useRef([])

  useEffect(() => {
    if (reducedMotion || activeStep === null) return undefined

    if (activeStep < loopSteps.length - 1) {
      const nextStep = window.setTimeout(() => setActiveStep((value) => value + 1), 3200)
      return () => window.clearTimeout(nextStep)
    }

    const showComplete = window.setTimeout(() => setAgentComplete(true), 2300)
    const beginReset = window.setTimeout(() => setTerminalResetting(true), 3800)
    const restart = window.setTimeout(() => {
      setActiveStep(null)
      setTypedPrompt('')
      setPromptSubmitted(false)
      setAgentComplete(false)
      setTerminalResetting(false)
      setCycle((value) => value + 1)
    }, 4250)

    return () => {
      window.clearTimeout(showComplete)
      window.clearTimeout(beginReset)
      window.clearTimeout(restart)
    }
  }, [activeStep, reducedMotion])

  useEffect(() => {
    if (reducedMotion) {
      setTypedPrompt(heroPrompt)
      setPromptSubmitted(true)
      setActiveStep(0)
      return undefined
    }

    setTypedPrompt('')
    setPromptSubmitted(false)
    setIntroAlertVisible(true)
    let interval
    let submit
    const start = window.setTimeout(() => {
      setIntroAlertVisible(false)
      let character = 0
      interval = window.setInterval(() => {
        character += 1
        setTypedPrompt(heroPrompt.slice(0, character))
        if (character >= heroPrompt.length) {
          window.clearInterval(interval)
          submit = window.setTimeout(() => {
            setPromptSubmitted(true)
            setActiveStep((value) => value ?? 0)
          }, 650)
        }
      }, 34)
    }, cycle === 0 ? 3100 : 1850)

    return () => {
      window.clearTimeout(start)
      window.clearTimeout(submit)
      window.clearInterval(interval)
    }
  }, [cycle, reducedMotion])

  useLayoutEffect(() => {
    const root = loopRef.current
    const transfer = transferRef.current
    const screen = screenRef.current
    const checkout = checkoutRef.current
    const proof = proofRefs.current[activeStep]

    if (activeStep === null || !root || !transfer || !screen || !proof) return undefined
    if (reducedMotion) {
      gsap.set(transfer, { opacity: 0 })
      return undefined
    }

    const source = activeStep === 1 && checkout ? proof : screen
    const destination = activeStep === 1 && checkout ? checkout : proof
    const rootBounds = root.getBoundingClientRect()
    const sourceBounds = source.getBoundingClientRect()
    const destinationBounds = destination.getBoundingClientRect()
    const transferBounds = transfer.getBoundingClientRect()
    const startX = sourceBounds.left - rootBounds.left + sourceBounds.width / 2 - transferBounds.width / 2
    const startY = sourceBounds.top - rootBounds.top + sourceBounds.height / 2 - transferBounds.height / 2
    const endX =
      destinationBounds.left - rootBounds.left + destinationBounds.width / 2 - transferBounds.width / 2
    const endY =
      destinationBounds.top - rootBounds.top + destinationBounds.height / 2 - transferBounds.height / 2
    const destinationScale =
      activeStep === 1 ? 0.58 : Math.max(0.18, Math.min(0.32, destinationBounds.width / transferBounds.width))

    const context = gsap.context(() => {
      const timeline = gsap.timeline()
      const initialScale = activeStep === 1 ? 0.54 : activeStep === 3 ? 0.82 : 1

      gsap.set(transfer, {
        x: startX,
        y: startY,
        scale: initialScale,
        rotate: activeStep === 1 ? -7 : 0,
        opacity: 0,
      })

      timeline
        .to(transfer, {
          opacity: 1,
          scale: activeStep === 1 ? 0.72 : initialScale,
          duration: 0.24,
          ease: 'power3.out',
        })
        .to(
          transfer,
          {
            x: endX,
            y: endY,
            scale: destinationScale,
            rotate: 0,
            duration: activeStep === 1 ? 0.72 : 0.88,
            ease: 'power4.inOut',
          },
          '+=0.46',
        )
        .to(transfer, { opacity: 0, duration: 0.16, ease: 'power2.in' })
        .fromTo(
          proof,
          { scale: 0.72, opacity: 0.35 },
          { scale: 1, opacity: 1, duration: 0.3, ease: 'power4.out' },
          '-=0.08',
        )
    })

    return () => context.revert()
  }, [activeStep, reducedMotion])

  return (
    <div
      className={`hero-loop hero-loop--step-${activeStep ?? 'idle'}`}
      ref={loopRef}
      aria-label="DevFlow agent loop demonstration"
    >
      <div className="hero-loop__agent">
        <div className="hero-loop__terminal-bar">
          <span aria-hidden="true">
            <i />
            <i />
            <i />
          </span>
          <span>
            <TerminalSquare size={14} aria-hidden="true" />
            coding-agent
          </span>
        </div>
        <div
          className={`hero-loop__prompt ${terminalResetting ? 'is-resetting' : ''}`}
          aria-label={`User prompt: ${heroPrompt}`}
        >
          <div className="hero-loop__request">
            <span className="hero-loop__speaker">USER</span>
            <span className="hero-loop__prompt-glyph" aria-hidden="true">
              ›
            </span>
            <strong aria-hidden="true">{typedPrompt}</strong>
            {!promptSubmitted && (
              <span
                className={`hero-loop__cursor ${typedPrompt.length === heroPrompt.length ? 'is-complete' : ''}`}
                aria-hidden="true"
              />
            )}
            <span
              className={`hero-loop__return ${typedPrompt.length === heroPrompt.length ? 'is-ready' : ''} ${
                promptSubmitted ? 'is-pressed' : ''
              }`}
              aria-hidden="true"
            >
              return ↵
            </span>
          </div>
          {promptSubmitted && activeStep !== null && (
            <div className={`hero-loop__agent-line ${agentComplete ? 'is-complete' : ''}`} key={agentComplete ? 'complete' : activeStep}>
              <span>AGENT</span>
              {agentComplete ? (
                <>
                  <Check size={14} aria-hidden="true" />
                  <strong>Complete</strong>
                </>
              ) : (
                <>
                  <strong>{loopSteps[activeStep].agentStatus}</strong>
                  <i aria-hidden="true" />
                </>
              )}
            </div>
          )}
        </div>
      </div>

      <div className={`device-mock device-mock--step-${activeStep ?? 'idle'}`}>
        <div className="device-mock__frame">
          <div className="device-mock__speaker" aria-hidden="true" />
          <div className="device-mock__screen" ref={screenRef}>
            <div className="mock-app__status">
              <span>9:41</span>
              <span>5G · 100%</span>
            </div>
            <header className="mock-app__header">
              <span>
                <ShoppingBag size={18} aria-hidden="true" />
                Trail Shop
              </span>
              <span className="mock-app__cart">1</span>
            </header>

            {activeStep === 3 ? (
              <div className="mock-app__success">
                <span>
                  <Check size={28} aria-hidden="true" />
                </span>
                <strong>Order confirmed</strong>
                <p>The checkout flow completed on the running app.</p>
                <small>Verified by DevFlow</small>
              </div>
            ) : (
              <div className="mock-app__content">
                <div className="mock-app__product">
                  <div className="mock-app__shoe" aria-hidden="true">
                    <span />
                  </div>
                  <div>
                    <strong>Trail Runner</strong>
                    <span>Slate · US 9</span>
                    <b>$129.00</b>
                  </div>
                </div>
                <div className="mock-app__summary">
                  <span>Subtotal</span>
                  <strong>$129.00</strong>
                </div>
                <button ref={checkoutRef} type="button">
                  Checkout
                </button>

                {activeStep === 0 && (
                  <>
                    <span className="tree-tag tree-tag--product">Grid #CartItem</span>
                    <span className="tree-tag tree-tag--button">Button #Checkout</span>
                  </>
                )}
                {activeStep === 1 && (
                  <span className="mock-app__pointer" aria-hidden="true">
                    <MousePointer2 size={26} />
                  </span>
                )}
                {activeStep === 2 && (
                  <div className="mock-app__error">
                    <Bug size={17} aria-hidden="true" />
                    <span>
                      <strong>POST /checkout · 500</strong>
                      <small>InvalidOperationException</small>
                    </span>
                  </div>
                )}
              </div>
            )}
            {activeStep === 3 && <span className="device-capture-flash" aria-hidden="true" />}
          </div>
        </div>
      </div>

      <ol className="hero-loop__steps">
        {loopSteps.map((step, index) => {
          const Icon = step.icon
          return (
            <li key={step.label}>
              <button
                className={activeStep === index ? 'is-active' : ''}
                type="button"
                aria-pressed={activeStep === index}
                onClick={() => {
                  setPromptSubmitted(true)
                  setAgentComplete(false)
                  setIntroAlertVisible(false)
                  setTerminalResetting(false)
                  setActiveStep(index)
                }}
              >
                <span className="hero-loop__step-icon">
                  <Icon size={18} aria-hidden="true" />
                </span>
                <span className="hero-loop__step-copy">
                  <strong>{step.label}</strong>
                  <code>{step.command}</code>
                </span>
                <span
                  className="hero-loop__step-proof"
                  ref={(node) => {
                    proofRefs.current[index] = node
                  }}
                  aria-hidden="true"
                >
                  <Icon size={13} />
                  <small>{step.proof}</small>
                </span>
              </button>
            </li>
          )
        })}
      </ol>

      {activeStep !== null && (
        <div
          className={`step-transfer step-transfer--${loopSteps[activeStep].transfer}`}
          ref={transferRef}
          aria-hidden="true"
        >
          <StepTransfer type={loopSteps[activeStep].transfer} />
        </div>
      )}

      {activeStep !== null && (
        <p className="sr-only" aria-live="polite">
          Step {activeStep + 1}: {loopSteps[activeStep].detail}
        </p>
      )}

      {introAlertVisible && (
        <div
          className={`hero-loop__moment hero-loop__alert ${cycle === 0 ? 'is-first-cycle' : ''}`}
          role="status"
          aria-live="polite"
        >
          <span className="hero-loop__moment-mark">
            <Bug size={42} aria-hidden="true" />
          </span>
          <strong>Bug detected</strong>
          <small>The checkout flow is failing in the running app.</small>
        </div>
      )}

      {agentComplete && (
        <div className="hero-loop__moment hero-loop__completion" role="status" aria-live="polite">
          <span className="hero-loop__moment-mark">
            <Check size={46} aria-hidden="true" />
          </span>
          <strong>Done</strong>
          <small>The fix is verified in the running app.</small>
        </div>
      )}
    </div>
  )
}

function Hero({ reducedMotion }) {
  return (
    <section className="hero" id="top" aria-labelledby="hero-title">
      <div className="hero__glow hero__glow--one" aria-hidden="true" />
      <div className="hero__glow hero__glow--two" aria-hidden="true" />

      <div className="hero__content page-shell">
        <div className="hero__copy">
          <p className="hero__label">
            <Radio size={16} aria-hidden="true" />
            THE RUNTIME LOOP FOR AI CODING AGENTS
          </p>
          <h1 id="hero-title">
            <span className="hero__line">Your agent can write the app.</span>
            <span className="hero__line hero__line--signal">Now it can use it, too.</span>
          </h1>
          <p className="hero__lede">
            MAUI DevFlow lets your coding agent see the live UI, operate native controls, inspect runtime evidence,
            and verify its own fixes on simulators and devices.
          </p>
          <div className="hero__actions">
            <MagneticLink href="#setup">
              Get DevFlow running
              <ArrowDown size={18} aria-hidden="true" />
            </MagneticLink>
            <MagneticLink href="#tools" variant="ghost">
              Explore the tool surface
              <Eye size={17} aria-hidden="true" />
            </MagneticLink>
          </div>
        </div>
        <HeroLoop reducedMotion={reducedMotion} />
      </div>
    </section>
  )
}

function Manifesto() {
  return (
    <section className="manifesto" aria-labelledby="manifesto-title">
      <div className="manifesto__orb manifesto__orb--one" aria-hidden="true" />
      <div className="manifesto__orb manifesto__orb--two" aria-hidden="true" />
      <div className="manifesto__scrim" aria-hidden="true" />
      <div className="manifesto__content page-shell">
        <div className="manifesto__copy">
          <div className="manifesto__message">
            <p>Code is only half the signal.</p>
            <h2 id="manifesto-title">
              Source can suggest a fix. <span>The running app can prove it.</span>
            </h2>
          </div>
          <p className="manifesto__body">
            Repository access helps an agent produce plausible code. DevFlow gives it the missing runtime context to
            reproduce the behavior, inspect what happened, and finish with observable proof.
          </p>
        </div>

        <div className="manifesto__comparison" aria-label="Repository-only agents compared with DevFlow runtime access">
          <div className="manifesto__lane manifesto__lane--source">
            <header>
              <span>
                <Code2 size={18} aria-hidden="true" />
                Repository access
              </span>
              <b>Not yet proven</b>
            </header>
            <ul>
              <li>
                <FileSearch size={17} aria-hidden="true" />
                Read and change source
              </li>
              <li>
                <Check size={17} aria-hidden="true" />
                Build and tests pass
              </li>
              <li className="manifesto__limit">
                <X size={17} aria-hidden="true" />
                No live UI or runtime evidence
              </li>
            </ul>
            <p>Stops at “the code looks right.”</p>
          </div>

          <div className="manifesto__bridge">
            <DevFlowMark className="manifesto__bridge-mark" />
            <span>
              <strong>MAUI DevFlow</strong>
              <small>opens the runtime</small>
            </span>
            <ChevronRight size={20} aria-hidden="true" />
          </div>

          <div className="manifesto__lane manifesto__lane--runtime">
            <header>
              <span>
                <Wifi size={18} aria-hidden="true" />
                Runtime access
              </span>
              <b>Verified</b>
            </header>
            <ul>
              <li>
                <Eye size={17} aria-hidden="true" />
                See and operate the native UI
              </li>
              <li>
                <Bug size={17} aria-hidden="true" />
                Capture logs and network evidence
              </li>
              <li>
                <Camera size={17} aria-hidden="true" />
                Re-run the flow and verify the screen
              </li>
            </ul>
            <p>
              <Check size={16} aria-hidden="true" />
              Finishes with observable proof.
            </p>
          </div>
        </div>
      </div>
    </section>
  )
}

function ToolRunway() {
  return (
    <section className="tools section" id="tools" aria-labelledby="tools-title">
      <div className="page-shell">
        <div className="tools__heading">
          <h2 id="tools-title">Everything your agent needs beyond the editor.</h2>
          <p>
            One coherent surface for seeing, operating, diagnosing, and extending the running app—available through 69
            structured MCP tools, the MAUI CLI, and the driver API.
          </p>
        </div>

        <div className="tool-runway">
          {toolGroups.map((group) => {
            const Icon = group.icon
            return (
              <article className="tool-card" key={group.verb}>
                <div className="tool-card__heading">
                  <span className="tool-card__icon">
                    <Icon size={26} aria-hidden="true" />
                  </span>
                  <div>
                    <span className="tool-card__verb">{group.verb}</span>
                    <h3>{group.title}</h3>
                  </div>
                </div>
                <p>{group.copy}</p>
                <ul aria-label={`${group.title} capabilities`}>
                  {group.tools.map((tool) => (
                    <li key={tool.label}>
                      <tool.icon size={15} aria-hidden="true" />
                      {tool.label}
                    </li>
                  ))}
                </ul>
              </article>
            )
          })}
        </div>

        <div className="platform-strip" aria-label="Platform and integration support">
          <span>iOS</span>
          <span>Android</span>
          <span>Mac Catalyst</span>
          <span>Windows</span>
          <span>macOS</span>
          <span>Linux / GTK</span>
          <span>Blazor Hybrid</span>
        </div>
      </div>
    </section>
  )
}

function Protocol() {
  return (
    <section className="protocol section" id="protocol" aria-labelledby="protocol-title">
      <div className="page-shell">
        <div className="protocol__intro">
          <h2 id="protocol-title">The coding loop, connected to the running app.</h2>
          <p>
            DevFlow sits between the AI coding agent and the live MAUI app, carrying actions in and returning evidence so
            the agent can evaluate, edit, and repeat until the code and the screen agree.
          </p>
        </div>

        <div
          className="workflow-map"
          role="img"
          aria-label="Source code flows to an AI coding agent, through MAUI DevFlow, into the running app. Runtime evidence returns to the AI agent."
        >
          <svg className="workflow-map__connections" viewBox="0 0 1200 390" aria-hidden="true">
            <defs>
              <marker
                id="workflow-arrow"
                markerHeight="10"
                markerUnits="userSpaceOnUse"
                markerWidth="10"
                orient="auto"
                refX="9"
                refY="5"
              >
                <path d="M0 0l10 5-10 5z" />
              </marker>
              <marker
                id="workflow-arrow-return"
                markerHeight="10"
                markerUnits="userSpaceOnUse"
                markerWidth="10"
                orient="auto"
                refX="9"
                refY="5"
              >
                <path d="M0 0l10 5-10 5z" />
              </marker>
            </defs>
            <path className="workflow-path" d="M245 132H314" markerEnd="url(#workflow-arrow)" />
            <path className="workflow-path workflow-path--primary" d="M545 132H604" markerEnd="url(#workflow-arrow)" />
            <path className="workflow-path workflow-path--primary" d="M835 132H894" markerEnd="url(#workflow-arrow)" />
            <path
              className="workflow-path workflow-path--return"
              d="M1060 225v43H455v-4"
              markerEnd="url(#workflow-arrow-return)"
            />
          </svg>
          <svg
            className="workflow-map__iteration"
            viewBox="0 0 1200 390"
            preserveAspectRatio="none"
            aria-hidden="true"
          >
            <defs>
              <marker
                id="workflow-arrow-iterate"
                markerHeight="10"
                markerUnits="userSpaceOnUse"
                markerWidth="10"
                orient="auto"
                refX="9"
                refY="5"
              >
                <path d="M0 0l10 5-10 5z" />
              </marker>
            </defs>
            <path
              className="workflow-path workflow-path--iterate"
              d="M756 342v38H160V246"
              markerEnd="url(#workflow-arrow-iterate)"
            />
          </svg>

          <article className="workflow-card workflow-card--code">
            <span className="workflow-card__icon">
              <Code2 size={25} aria-hidden="true" />
            </span>
            <span className="workflow-card__label">SOURCE</span>
            <h3>App code</h3>
            <p>The agent edits the project and starts a fresh debug build.</p>
            <code>CheckoutService.cs</code>
          </article>

          <article className="workflow-card workflow-card--ai">
            <span className="workflow-card__icon">
              <Bot size={25} aria-hidden="true" />
            </span>
            <span className="workflow-card__label">AI AGENT</span>
            <h3>Plans and acts</h3>
            <p>Your existing coding agent calls structured tools instead of guessing.</p>
            <span className="workflow-card__status">
              <RotateCw size={14} aria-hidden="true" />
              Ready to iterate
            </span>
          </article>

          <article className="workflow-card workflow-card--devflow">
            <span className="workflow-card__icon">
              <DevFlowMark />
            </span>
            <span className="workflow-card__label">MAUI DEVFLOW</span>
            <h3>Bridges runtime</h3>
            <p>MCP, CLI, and driver APIs connect the coding loop directly to the app.</p>
            <span className="workflow-card__count">69 structured tools</span>
          </article>

          <article className="workflow-card workflow-card--app">
            <span className="workflow-card__icon">
              <Smartphone size={25} aria-hidden="true" />
            </span>
            <span className="workflow-card__label">RUNNING APP</span>
            <h3>Shows the truth</h3>
            <p>Native controls, visual state, and diagnostics stay available live.</p>
            <span className="workflow-card__app-state">
              <Check size={15} aria-hidden="true" />
              Debug agent connected
            </span>
          </article>

          <div className="workflow-evidence">
            <div className="workflow-evidence__heading">
              <span>
                <Check size={18} aria-hidden="true" />
              </span>
              <div>
                <strong>Runtime evidence returns to the AI</strong>
                <small>It evaluates the result, refines the code, and keeps the loop moving.</small>
              </div>
            </div>
            <ul aria-label="Evidence returned from the running app and the next iteration">
              <li>
                <Layers3 size={15} aria-hidden="true" />
                Visual tree
              </li>
              <li>
                <Camera size={15} aria-hidden="true" />
                Screenshots
              </li>
              <li>
                <Network size={15} aria-hidden="true" />
                Network
              </li>
              <li>
                <ScrollText size={15} aria-hidden="true" />
                Logs
              </li>
              <li className="workflow-evidence__next">
                <RotateCw size={15} aria-hidden="true" />
                AI iterates again
              </li>
            </ul>
          </div>
        </div>
      </div>
    </section>
  )
}

function CopyButton({ value, label, className = '' }) {
  const [state, setState] = useState('idle')

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value)
      setState('copied')
      window.setTimeout(() => setState('idle'), 1800)
    } catch {
      setState('error')
    }
  }

  return (
    <>
      <button className={className} type="button" onClick={copy} aria-label={`Copy ${label}`}>
        {state === 'copied' ? <Check size={17} /> : <Clipboard size={17} />}
        <span>{state === 'copied' ? 'Copied' : state === 'error' ? 'Copy unavailable' : 'Copy'}</span>
      </button>
      <span className="sr-only" aria-live="polite">
        {state === 'copied' ? `${label} copied to clipboard` : state === 'error' ? `${label} could not be copied` : ''}
      </span>
    </>
  )
}

function CopyCode({ children, label }) {
  return (
    <div className="code-block">
      <pre>
        <code>{children}</code>
      </pre>
      <CopyButton value={children} label={label} />
    </div>
  )
}

function Setup() {
  return (
    <section className="setup section" id="setup" aria-labelledby="setup-title">
      <div className="page-shell">
        <div className="setup__heading">
          <div>
            <h2 id="setup-title">Give your agent a running start.</h2>
            <p>Install the CLI, initialize DevFlow, then let your coding agent handle the app integration.</p>
          </div>
          <div className="setup__heading-actions">
            <MagneticLink href="https://learn.microsoft.com/dotnet/maui/developer-tools/devflow/" variant="dark" external>
              Read the source docs
              <ArrowUpRight size={18} aria-hidden="true" />
            </MagneticLink>
            <MagneticLink href="https://github.com/dotnet/maui-labs" variant="outline" external>
              <Github size={18} aria-hidden="true" />
              GitHub
            </MagneticLink>
          </div>
        </div>

        <div className="setup-terminal" aria-label="MAUI DevFlow quick start terminal">
          <div className="setup-terminal__bar">
            <span className="setup-terminal__lights" aria-hidden="true">
              <i />
              <i />
              <i />
            </span>
            <span className="setup-terminal__title">
              <TerminalSquare size={14} aria-hidden="true" />
              devflow-setup
            </span>
            <span className="setup-terminal__mode">QUICK START</span>
          </div>
          <ol className="setup-terminal__steps">
            {quickSetupSteps.map((step) => (
              <li key={step.number}>
                <div className="setup-terminal__meta">
                  <span>{step.number}</span>
                  <strong>{step.label}</strong>
                </div>
                <div className={`setup-terminal__line setup-terminal__line--${step.type}`}>
                  <span className="setup-terminal__prompt" aria-hidden="true">
                    {step.type === 'prompt' ? 'USER' : '$'}
                  </span>
                  <code>{step.value}</code>
                  <CopyButton
                    className="setup-terminal__copy"
                    value={step.value}
                    label={`${step.label} ${step.type === 'prompt' ? 'prompt' : 'command'}`}
                  />
                </div>
              </li>
            ))}
          </ol>
          <div className="setup-terminal__ready">
            <Check size={16} aria-hidden="true" />
            Your agent has everything it needs to finish the integration.
          </div>
        </div>

        <details className="setup__manual">
          <summary>
            <span className="setup__manual-icon">
              <TerminalSquare size={21} aria-hidden="true" />
            </span>
            <span>
              <strong>Manual Setup</strong>
              <small>Package registration, CLI diagnostics, and direct runtime commands.</small>
            </span>
            <ChevronDown size={22} aria-hidden="true" />
          </summary>
          <div className="setup__manual-content">
            <ol className="setup__steps">
              {manualSetupSteps.map((step) => (
                <li key={step.number}>
                  <div className="setup-step__title">
                    <span>{step.number}</span>
                    <div>
                      <h3>{step.title}</h3>
                      <p>{step.description}</p>
                    </div>
                  </div>
                  <div className="setup-step__commands">
                    <CopyCode label={`${step.title} command`}>{step.command}</CopyCode>
                    <CopyCode label={`${step.title} code`}>{step.code}</CopyCode>
                  </div>
                </li>
              ))}
            </ol>
          </div>
        </details>

        <div className="setup__finish">
          <div>
            <span className="status status--live">LOOP CLOSED</span>
            <h3>Build. Run. Let the agent look.</h3>
          </div>
          <div className="setup__links">
            <MagneticLink href="https://github.com/dotnet/maui-labs" variant="dark" external>
              <Github size={18} aria-hidden="true" />
              View on GitHub
            </MagneticLink>
            <MagneticLink
              href="https://www.nuget.org/packages/Microsoft.Maui.DevFlow.Agent/"
              variant="outline"
              external
            >
              View NuGet package
              <ArrowUpRight size={18} aria-hidden="true" />
            </MagneticLink>
          </div>
        </div>
      </div>
    </section>
  )
}

function Footer() {
  return (
    <footer className="footer">
      <div className="page-shell">
        <div className="footer__main">
          <div className="footer__brand">
            <DevFlowMark />
            <div>
              <strong>MAUI DEVFLOW</strong>
              <span>Runtime eyes and hands for AI coding agents.</span>
            </div>
          </div>
          <div className="footer__links">
            <a href="https://github.com/dotnet/maui-labs" target="_blank" rel="noreferrer">
              <Github size={17} aria-hidden="true" />
              GitHub
            </a>
            <a href="#tools">Capabilities</a>
            <a href="#protocol">How it works</a>
            <a href="#setup">Setup</a>
          </div>
        </div>
        <div className="footer__meta">
          <span className="footer__status">
            <span aria-hidden="true" />
            EXPERIMENTAL / ACTIVELY DEVELOPED
          </span>
          <a href="https://github.com/dotnet/maui-labs/blob/main/LICENSE" target="_blank" rel="noreferrer">
            Copyright © .NET Foundation and Contributors
          </a>
        </div>
      </div>
    </footer>
  )
}

export default function App() {
  const rootRef = useRef(null)
  const reducedMotion = useReducedMotion()
  const [activeSection, setActiveSection] = useState('')

  useEffect(() => {
    const sections = document.querySelectorAll('section[id]')
    const observer = new IntersectionObserver(
      (entries) => {
        const visible = entries
          .filter((entry) => entry.isIntersecting)
          .sort((a, b) => b.intersectionRatio - a.intersectionRatio)[0]
        if (visible) setActiveSection(visible.target.id)
      },
      { rootMargin: '-30% 0px -55%', threshold: [0, 0.15, 0.4, 0.7] },
    )

    sections.forEach((section) => observer.observe(section))
    return () => observer.disconnect()
  }, [])

  useEffect(() => {
    const root = rootRef.current
    const navbar = root.querySelector('[data-navbar]')

    const context = gsap.context(() => {
      if (!reducedMotion) {
        const heroTimeline = gsap.timeline({ defaults: { ease: 'power3.out' } })
        heroTimeline
          .from('.hero__label', { y: 24, opacity: 0, duration: 0.55 })
          .from('.hero__line', { yPercent: 105, opacity: 0, duration: 0.78, stagger: 0.08 }, '-=0.2')
          .from('.hero__lede, .hero__actions', { y: 24, opacity: 0, duration: 0.55, stagger: 0.08 }, '-=0.35')
          .from('.hero-loop', { x: 42, opacity: 0, filter: 'blur(10px)', duration: 0.72 }, '-=0.5')

        gsap.to('.manifesto__orb--one', {
          yPercent: 18,
          xPercent: -12,
          ease: 'none',
          scrollTrigger: {
            trigger: '.manifesto',
            start: 'top bottom',
            end: 'bottom top',
            scrub: 0.8,
          },
        })

        gsap.from('.workflow-card, .workflow-evidence', {
          y: 24,
          opacity: 0,
          duration: 0.55,
          stagger: 0.08,
          ease: 'power3.out',
          scrollTrigger: {
            trigger: '.workflow-map',
            start: 'top 76%',
          },
        })
      }

      ScrollTrigger.create({
        trigger: '.hero',
        start: 'bottom 90px',
        onEnter: () => navbar.classList.add('is-scrolled'),
        onLeaveBack: () => navbar.classList.remove('is-scrolled'),
      })
    }, root)

    return () => context.revert()
  }, [reducedMotion])

  return (
    <div className="site" ref={rootRef}>
      <a className="skip-link" href="#main-content">
        Skip to main content
      </a>
      <Navbar activeSection={activeSection} />
      <main id="main-content">
        <Hero reducedMotion={reducedMotion} />
        <Manifesto />
        <ToolRunway />
        <Protocol />
        <Setup />
      </main>
      <Footer />
    </div>
  )
}
