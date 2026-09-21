/* Tappy website demo. This only updates the page: no device access or output. */
(() => {
  'use strict';

  const menuButton = document.querySelector('.menu-toggle');
  const navigation = document.getElementById('main-nav');

  if (menuButton && navigation) {
    document.body.classList.add('enhanced');
    menuButton.hidden = false;

    const closeMenu = () => {
      menuButton.setAttribute('aria-expanded', 'false');
      navigation.classList.remove('is-open');
    };

    menuButton.addEventListener('click', () => {
      const isOpen = menuButton.getAttribute('aria-expanded') === 'true';
      menuButton.setAttribute('aria-expanded', String(!isOpen));
      navigation.classList.toggle('is-open', !isOpen);
    });

    navigation.addEventListener('click', (event) => {
      if (event.target.closest('a')) closeMenu();
    });

    document.addEventListener('keydown', (event) => {
      if (event.key === 'Escape' && menuButton.getAttribute('aria-expanded') === 'true') {
        closeMenu();
        menuButton.focus();
      }
    });

    document.addEventListener('click', (event) => {
      if (!event.target.closest('.site-header')) closeMenu();
    });

    window.matchMedia('(min-width: 981px)').addEventListener('change', closeMenu);
  }

  const keyButtons = [...document.querySelectorAll('.demo-key')];
  const workflowButtons = [...document.querySelectorAll('.workflow-button')];
  const title = document.getElementById('assignment-title');
  const description = document.getElementById('assignment-description');
  const label = document.getElementById('assignment-label');
  const stepList = document.getElementById('assignment-steps');
  const highlight = document.getElementById('photo-highlight');
  const locatorLabel = document.getElementById('locator-key-label');
  if (!keyButtons.length || !title || !description || !label || !stepList || !highlight || !locatorLabel) return;

  const keyboard = (name, shortcut, detail) => ({ name, shortcut, detail, type: 'Keyboard', steps: [shortcut] });
  const sequence = (name, type, detail, steps) => ({ name, shortcut: `${steps.length}-step sequence`, detail, type, steps });
  const midiNote = (name, note) => sequence(name, 'MIDI', `An example note on a configured MIDI output. The receiving instrument supplies the sound.`, [`MIDI note on · Ch 1 · Note ${note} · Velocity 100`, 'Delay · 250 ms', `MIDI note off · Ch 1 · Note ${note}`]);

  // Examples illustrate the assignment types. They are not shipped app presets.
  const workflows = {
    editing: [
      keyboard('Copy', 'Ctrl + C', 'Put the selected text or item on the clipboard.'),
      keyboard('Cut', 'Ctrl + X', 'Cut the selected text or item so it is ready to move.'),
      keyboard('Paste', 'Ctrl + V', 'Place clipboard content in the active application.'),
      keyboard('Undo', 'Ctrl + Z', 'Take a step back in an application that supports this shortcut.'),
      keyboard('Redo', 'Ctrl + Y', 'Redo the previous change where this shortcut is supported.'),
      keyboard('Save', 'Ctrl + S', 'Keep the latest changes to the file you are working on.'),
      keyboard('Save as', 'Ctrl + Shift + S', 'Open Save As in software that uses this shortcut.'),
      keyboard('Find', 'Ctrl + F', 'Get to the search box without reaching for a menu.'),
      keyboard('Select all', 'Ctrl + A', 'Select everything in the current editing area.'),
      keyboard('New', 'Ctrl + N', 'Start something new in the active application.'),
      keyboard('Open', 'Ctrl + O', 'Open the file chooser in compatible applications.'),
      sequence('Save + pause', 'Keyboard + delay', 'A simple two-step example. Add an explicit pause before the next part of a longer routine.', ['Ctrl + S', 'Delay · 250 ms'])
    ],
    studio: [
      midiNote('Note 60', 60),
      midiNote('Note 64', 64),
      midiNote('Note 67', 67),
      sequence('Three-note run', 'MIDI + delay', 'A short musical sequence through your configured MIDI output. Timing and sound depend on the receiver.', ['MIDI note on · Ch 1 · Note 60 · Velocity 100', 'Delay · 150 ms', 'MIDI note off · Ch 1 · Note 60', 'MIDI note on · Ch 1 · Note 64 · Velocity 100', 'Delay · 150 ms', 'MIDI note off · Ch 1 · Note 64', 'MIDI note on · Ch 1 · Note 67 · Velocity 100', 'Delay · 150 ms', 'MIDI note off · Ch 1 · Note 67']),
      { name: 'CC 1 high', shortcut: 'MIDI CC · 127', type: 'MIDI', detail: 'Send a control-change value. Its meaning depends on your instrument or MIDI mapping.', steps: ['MIDI CC · Ch 1 · Controller 1 · Value 127'] },
      { name: 'CC 1 low', shortcut: 'MIDI CC · 0', type: 'MIDI', detail: 'Send the other end of a control-change range through a configured MIDI output.', steps: ['MIDI CC · Ch 1 · Controller 1 · Value 0'] },
      { name: 'Program 1', shortcut: 'MIDI program change', type: 'MIDI', detail: 'Choose a program on a receiving instrument. Device display numbering may differ from the MIDI value.', steps: ['MIDI program change · Ch 1 · Value 0'] },
      { name: 'OSC cue', shortcut: '/cue/go · 1', type: 'OSC / UDP', detail: 'An example OSC message. Configure the address, host, and port to match your listening application.', steps: ['OSC → configured receiver · /cue/go · integer 1'] },
      keyboard('Play / pause', 'Space', 'A common transport shortcut when your music application uses Space for play and pause.'),
      keyboard('Save session', 'Ctrl + S', 'Use your application’s save shortcut from a dedicated key.'),
      { name: 'Session note', shortcut: 'Type a text snippet', type: 'Text', detail: 'Add a reusable note to the currently focused text field.', steps: ['Type text · Take notes: '] },
      sequence('Save + cue', 'Keyboard + OSC', 'Combine assignment types in one routine, with an explicit delay between them.', ['Ctrl + S', 'Delay · 500 ms', 'OSC → configured receiver · /cue/saved · integer 1'])
    ],
    everyday: [
      keyboard('Explorer', 'Win + E', 'Open Windows File Explorer from a spare key.'),
      keyboard('Desktop', 'Win + D', 'Show or hide the Windows desktop.'),
      keyboard('Search', 'Win + S', 'Open Windows Search.'),
      keyboard('Settings', 'Win + I', 'Open Windows Settings.'),
      keyboard('Screenshot', 'Win + Shift + S', 'Open the Windows screen-snipping controls.'),
      keyboard('Emoji picker', 'Win + .', 'Bring up the Windows emoji and symbols picker.'),
      keyboard('New tab', 'Ctrl + T', 'Open a new tab in a compatible application.'),
      keyboard('Reopen tab', 'Ctrl + Shift + T', 'Reopen a closed tab where this shortcut is supported.'),
      keyboard('Address bar', 'Ctrl + L', 'Move focus to the address bar in a compatible browser.'),
      { name: 'Quick reply', shortcut: 'Type a text snippet', type: 'Text', detail: 'A small phrase you can reuse in a focused text field.', steps: ['Type text · Thanks! I’ll take a look and get back to you.'] },
      { name: 'Open a project', shortcut: 'Launch a chosen file', type: 'Program / document', detail: 'Use an assignment to open a document you choose on your own computer.', steps: ['Open → your selected local project file'] },
      sequence('Find a phrase', 'Keyboard + text', 'Open Find, pause briefly, then enter a reusable search term in a compatible application.', ['Ctrl + F', 'Delay · 200 ms', 'Type text · project notes'])
    ]
  };

  // Same photo geometry as Tappy.App/Services/ControllerPhotoCatalog.cs.
  // Source coordinates are transformed into the tight 1174 × 1339 photo.
  const photoKeys = [
    [91, 300, 86, 78], [181, 305, 86, 78], [276, 306, 86, 78],
    [374, 308, 86, 78], [472, 306, 86, 78], [570, 305, 86, 78],
    [668, 300, 89, 78], [78, 388, 98, 82], [177, 394, 92, 82],
    [274, 398, 92, 82], [372, 400, 92, 82], [470, 399, 92, 82]
  ];

  let activeWorkflow = 'editing';
  let selectedKey = 0;

  const selectKey = (index) => {
    if (!Number.isInteger(index) || index < 0 || index >= photoKeys.length) return;
    selectedKey = index;
    const assignment = workflows[activeWorkflow][index];
    keyButtons.forEach((button, position) => button.setAttribute('aria-pressed', String(position === index)));
    label.textContent = `G${index + 1} / ${assignment.type.toUpperCase()}`;
    title.textContent = assignment.name;
    description.textContent = assignment.detail;
    stepList.replaceChildren(...assignment.steps.map((step, position) => {
      const item = document.createElement('li');
      const number = document.createElement('span');
      const text = document.createElement('code');
      number.textContent = String(position + 1).padStart(2, '0');
      text.textContent = step;
      item.append(number, text);
      return item;
    }));

    const [left, top, width, height] = photoKeys[index];
    highlight.style.left = `${((90 + left * 1.16) / 1174) * 100}%`;
    highlight.style.top = `${((top * 1.05) / 1339) * 100}%`;
    highlight.style.width = `${((width * 1.16) / 1174) * 100}%`;
    highlight.style.height = `${((height * 1.05) / 1339) * 100}%`;
    locatorLabel.textContent = `G${index + 1} selected`;
  };

  const selectWorkflow = (name) => {
    if (!Object.hasOwn(workflows, name)) return;
    activeWorkflow = name;
    workflowButtons.forEach((button) => button.setAttribute('aria-pressed', String(button.dataset.workflow === name)));
    keyButtons.forEach((button, index) => {
      const assignment = workflows[name][index];
      button.querySelector('strong').textContent = assignment.name;
      button.querySelector('.key-shortcut').textContent = assignment.shortcut;
    });
    selectKey(selectedKey);
  };

  keyButtons.forEach((button) => button.addEventListener('click', () => selectKey(Number(button.dataset.key))));
  workflowButtons.forEach((button) => button.addEventListener('click', () => selectWorkflow(button.dataset.workflow)));
  selectWorkflow(activeWorkflow);
})();
