const BASE = 'http://localhost:9000/rf/api';

async function login() {
  const res = await fetch(`${BASE}/login`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ email: 'admin@karasoftware.com', password: '123456' }),
  });
  const cookies = res.headers.getSetCookie?.() ?? [];
  const cookieHeader = cookies.map(c => c.split(';')[0]).join('; ');
  const data = await res.json();
  return { token: data.token, cookieHeader };
}

function rand(arr) { return arr[Math.floor(Math.random() * arr.length)]; }
function randInt(min, max) { return Math.floor(Math.random() * (max - min + 1)) + min; }
function randBool() { return Math.random() > 0.5; }
function randDate(startYear = 2024, endYear = 2027) {
  const y = randInt(startYear, endYear);
  const m = String(randInt(1, 12)).padStart(2, '0');
  const d = String(randInt(1, 28)).padStart(2, '0');
  return `${y}${m}${d}`;
}

const objectiveAdjectives = ['Strategic', 'Critical', 'Innovative', 'Agile', 'Sustainable', 'Resilient', 'Transformative', 'Scalable', 'Dynamic', 'Proactive'];
const objectiveNouns = ['Growth', 'Revenue', 'Engagement', 'Performance', 'Efficiency', 'Quality', 'Delivery', 'Adoption', 'Retention', 'Optimization'];
const objectiveVerbs = ['Improve', 'Accelerate', 'Maximize', 'Enhance', 'Streamline', 'Strengthen', 'Drive', 'Expand', 'Reduce', 'Increase'];
const domains = ['Sales', 'Engineering', 'Marketing', 'Product', 'Operations', 'Customer Success', 'HR', 'Finance', 'Security', 'Data'];
const comments = [
  'Making good progress on this objective.',
  'We need to revisit the timeline.',
  'Dependencies are being resolved.',
  'Team alignment looks solid.',
  'Key stakeholders have been notified.',
  'Blockers identified and being addressed.',
  'On track for completion.',
  'Additional resources may be needed.',
  'Milestone reached ahead of schedule.',
  'Risk assessment completed.'
];
const rootCauses = [
  'Customer feedback indicated a gap in our offering.',
  'Market analysis revealed competitive pressure.',
  'Internal audit identified process inefficiencies.',
  'Team retrospective highlighted improvement areas.',
  'Quarterly review surfaced key bottlenecks.',
  'User analytics showed declining engagement.',
  'Cost analysis pointed to optimization opportunities.',
  'Regulatory changes require adaptation.',
  'Technology debt accumulated over multiple sprints.',
  'Stakeholder interviews surfaced unmet needs.'
];
const surveyTitles = ['Employee Satisfaction', 'Customer Feedback', 'Product Usability', 'Training Effectiveness', 'Workplace Culture', 'Service Quality', 'Feature Prioritization', 'Onboarding Experience', 'Team Collaboration', 'Process Improvement'];
const surveyDescriptions = [
  'Help us understand your experience and identify areas for improvement.',
  'Your feedback is crucial for our continuous improvement efforts.',
  'Please share your honest opinions to help shape our future direction.',
  'This survey aims to gather insights for better decision-making.',
  'We value your input in making our environment better for everyone.',
];
const questionTexts = [
  'How satisfied are you with the current process?',
  'What improvements would you suggest?',
  'Rate your overall experience from 1-10.',
  'Do you feel supported in your role?',
  'How effective is the communication in your team?',
  'What challenges have you faced recently?',
  'Would you recommend this to a colleague?',
  'How clear are the goals and expectations?',
  'What resources do you need to be more effective?',
  'How well does the team collaborate?',
  'Are meeting schedules effective?',
  'How responsive is management to feedback?',
  'Rate the quality of tools provided.',
  'Is the workload distribution fair?',
  'How valuable is the training provided?',
];
const choiceLabels = [
  ['Strongly Agree', 'Agree', 'Neutral', 'Disagree', 'Strongly Disagree'],
  ['Excellent', 'Good', 'Average', 'Below Average', 'Poor'],
  ['Very Satisfied', 'Satisfied', 'Neutral', 'Dissatisfied', 'Very Dissatisfied'],
  ['Always', 'Often', 'Sometimes', 'Rarely', 'Never'],
  ['Yes', 'No', 'Maybe'],
];

async function createEntity(auth, entityName, body) {
  const res = await fetch(`${BASE}/crud?operation=CREATE&type=${entityName}`, {
    method: 'POST',
    headers: {
      'Content-Type': 'application/json',
      Authorization: `Bearer ${auth.token}`,
      Cookie: auth.cookieHeader,
    },
    body: JSON.stringify(body),
  });
  if (!res.ok) {
    const text = await res.text();
    throw new Error(`POST ${entityName} ${res.status}: ${text}`);
  }
  return res.json();
}

function makeObjective(i) {
  const title = { rendered: `${rand(objectiveVerbs)} ${rand(domains)} ${rand(objectiveNouns)} ${i + 1}-${Date.now().toString(36)}` };
  const keyResultCount = randInt(1, 4);
  const key_results = [];
  for (let k = 0; k < keyResultCount; k++) {
    const commentCount = randInt(0, 2);
    const key_result_comments = [];
    for (let c = 0; c < commentCount; c++) {
      key_result_comments.push({ author: 2, comment: rand(comments) });
    }
    key_results.push({
      key_result: `${rand(objectiveAdjectives)} key result for ${rand(domains).toLowerCase()} initiative ${k + 1}`,
      key_result_comments,
      achieved: randBool(),
    });
  }

  const commentCount = randInt(0, 3);
  const objective_comments = [];
  for (let c = 0; c < commentCount; c++) {
    objective_comments.push({ author: 2, comment: rand(comments) });
  }

  return {
    title,
    author: 2,
    tags: [],
    categories: [],
    fields: {
      objective_work_start_date: randDate(),
      objective_type: rand(['short_term', 'long_term']),
      documentation_url: randBool() ? `https://docs.example.com/obj-${i + 1}` : '',
      root_cause: `${rand(rootCauses)} (Objective ${i + 1})`,
      creator_comment: { author: 2, comment: rand(comments) },
      key_results,
      objective_comments,
      year_based_okr_type: '',
      objective_initiation_year: rand(['-1', '2025', '2026', '2027']),
    },
  };
}

function makeSurvey(i) {
  const title = { rendered: `${rand(surveyTitles)} Survey Q${randInt(1, 4)} ${2025 + Math.floor(i / 15)} ${i + 1}-${Date.now().toString(36)}` };
  const sectionCount = randInt(1, 3);
  const sections = [];
  for (let s = 0; s < sectionCount; s++) {
    const questionCount = randInt(2, 5);
    const questions = [];
    for (let q = 0; q < questionCount; q++) {
      const qType = rand(['text', 'choice', 'rating']);
      const question = {
        question_text: rand(questionTexts),
        question_type: qType,
        is_required: randBool(),
        help_text: randBool() ? 'Please answer honestly.' : '',
      };
      if (qType === 'rating') {
        question.min_rating = randInt(0, 1);
        question.max_rating = randInt(5, 10);
      }
      if (qType === 'choice') {
        const labels = rand(choiceLabels);
        question.choices = labels.map((label, ci) => ({
          choice_label: label,
          is_correct: ci === 0,
          choice_score: randInt(0, 10),
        }));
      }
      questions.push(question);
    }
    const hasScoring = randBool();
    sections.push({
      section_title: `Section ${s + 1}: ${rand(domains)}`,
      section_description: rand(surveyDescriptions),
      has_scoring: hasScoring,
      passing_score: hasScoring ? randInt(50, 90) : 0,
      scoring_mode: hasScoring ? rand(['simple', 'weighted']) : 'simple',
      score_explanation: hasScoring ? 'Score is calculated based on selected answers.' : '',
      questions,
    });
  }

  return {
    title,
    author: 2,
    fields: {
      survey_description: rand(surveyDescriptions),
      is_anonymous: randBool(),
      response_limit: randInt(10, 100),
      due_date: randDate(2025, 2027),
      survey_status: rand(['draft', 'active', 'closed']),
      sections,
    },
  };
}

async function main() {
  console.log('Logging in...');
  const auth = await login();
  console.log('Logged in.');

  // Create 110 objectives
  console.log('Creating 110 objectives...');
  for (let i = 0; i < 110; i++) {
    try {
      const obj = makeObjective(i);
      await createEntity(auth, 'objective', obj);
      if ((i + 1) % 10 === 0) console.log(`  Objectives: ${i + 1}/110`);
    } catch (e) {
      console.error(`  Objective ${i + 1} failed: ${e.message}`);
    }
  }

  // Create 55 surveys
  console.log('Creating 55 surveys...');
  for (let i = 0; i < 55; i++) {
    try {
      const survey = makeSurvey(i);
      await createEntity(auth, 'survey', survey);
      if ((i + 1) % 10 === 0) console.log(`  Surveys: ${i + 1}/55`);
    } catch (e) {
      console.error(`  Survey ${i + 1} failed: ${e.message}`);
    }
  }

  console.log('Done! Created 110 objectives and 55 surveys.');
}

main().catch(console.error);
