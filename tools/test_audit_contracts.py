import unittest
from audit_contracts import contracts

class ContractParserTests(unittest.TestCase):
    def test_expression_body_ignores_solver_changes(self):
        source = 'protected override void RegisterOutputParams(P p) => p.Add("SVG", 1); protected void SolveInstance(D d) { d.Set(0); }'
        self.assertEqual(contracts(source), contracts(source.replace('d.Set(0);', 'if (true) { d.Clear(); }')))
        self.assertNotEqual(contracts(source), contracts(source.replace('"SVG"', '"PNG"')))

    def test_block_body_and_comments(self):
        source = 'void RegisterInputParams(P p) { p.Add("Input"); } void SolveInstance(D d) { d.Clear(); }'
        self.assertEqual(contracts(source), contracts(source.replace('p.Add', '/* ignored */ p.Add')))
        self.assertNotEqual(contracts(source), contracts(source.replace('"Input"', '"Other"')))

    def test_multiple_registrations_and_nested_expression(self):
        source = 'void RegisterInputParams(P p) { p.Add("I"); } void RegisterOutputParams(P p) => p.Add(() => { return "O"; }); void Next() { x(); }'
        self.assertEqual(len(contracts(source)), 2)
        self.assertEqual(contracts(source), contracts(source.replace('x();', 'y();')))
        self.assertNotEqual(contracts(source), contracts(source.replace('"O"', '"Q"')))

if __name__ == '__main__':
    unittest.main()
